using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.Immutable;

namespace ReactiveGenerator.Tests;

public static class SourceGeneratorTestHelper
{
        /// <summary>
    /// A specialized cross-assembly test method that actually runs the same generators
    /// on the external assembly first, ensuring any [Reactive] classes in the external
    /// assembly get physically compiled with INotifyPropertyChanged.
    /// 
    /// Then references that compiled assembly from the main assembly, so the derived
    /// class sees that the base physically has INPC, and thus won't generate a second partial.
    /// </summary>
public static async Task TestCrossAssemblyAndVerifyWithExternalGen(
    string externalAssemblySource,
    string mainAssemblySource,
    Dictionary<string, string>? analyzerConfigOptions = null,
    params IIncrementalGenerator[] generators)
{
    // 1. Build external assembly
    var externalCompilation = CreateCompilation(externalAssemblySource, "ExternalAssembly");
    // 2. Create generator driver
    GeneratorDriver extDriver = CSharpGeneratorDriver.Create(
        generators.Select(g => g.AsSourceGenerator()),
        parseOptions: (CSharpParseOptions)externalCompilation.SyntaxTrees.First().Options,
        optionsProvider: analyzerConfigOptions != null
            ? new DictionaryAnalyzerConfigOptionsProvider(analyzerConfigOptions)
            : null);

    // 3. Run on external assembly
    extDriver = extDriver.RunGenerators(externalCompilation);

    var extRunResult = extDriver.GetRunResult();

    // 4. Add newly generated syntax trees => includes .ReactiveProperties.g.cs
    externalCompilation = externalCompilation.AddSyntaxTrees(extRunResult.GeneratedTrees);

    // (Optional) you can re-check for newly discovered partials, but typically once is enough.

    // 5. Create reference from the updated external compilation
    var externalReference = externalCompilation.ToMetadataReference();

    // 6. Create main assembly referencing the updated external assembly
    var mainCompilation = CreateCompilation(mainAssemblySource, "MainAssembly", externalReference);

    // 7. Run generator on the main assembly
    GeneratorDriver mainDriver = CSharpGeneratorDriver.Create(
        generators.Select(g => g.AsSourceGenerator()),
        parseOptions: (CSharpParseOptions)mainCompilation.SyntaxTrees.First().Options,
        optionsProvider: analyzerConfigOptions != null
            ? new DictionaryAnalyzerConfigOptionsProvider(analyzerConfigOptions)
            : null);

    mainDriver = mainDriver.RunGenerators(mainCompilation);

    // 8. Verify final results
    await Verify(mainDriver);
}


    /// <summary>
    /// Single-assembly test entry point (existing).
    /// </summary>
    public static Task TestAndVerify(
        string source,
        Dictionary<string, string>? analyzerConfigOptions = null,
        [CallerMemberName] string? testName = null,
        params IIncrementalGenerator[] generators)
    {
        if (generators == null || generators.Length == 0)
        {
            throw new ArgumentException("At least one generator must be provided", nameof(generators));
        }

        // Create single-assembly compilation
        var compilation = CreateCompilation(source);

        // Create the driver with generators and config
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators.Select(g => g.AsSourceGenerator()),
            parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options,
            optionsProvider: analyzerConfigOptions != null
                ? new DictionaryAnalyzerConfigOptionsProvider(analyzerConfigOptions)
                : null);

        // Run generation
        driver = driver.RunGenerators(compilation);

        // Verify
        return Verify(driver);
    }

    /// <summary>
    /// NEW: Cross-assembly testing for scenarios where a base class (with or without [Reactive]) 
    /// resides in a separate assembly.
    /// </summary>
    /// <param name="externalAssemblySource">Source code for the external assembly (defines base, attributes, etc.)</param>
    /// <param name="mainAssemblySource">Source code for the main assembly that references the external assembly.</param>
    /// <param name="analyzerConfigOptions">Optional analyzer config (e.g. UseBackingFields=true).</param>
    /// <param name="generators">Your incremental generators.</param>
    /// <returns></returns>
    public static Task TestCrossAssemblyAndVerify(
        string externalAssemblySource,
        string mainAssemblySource,
        Dictionary<string, string>? analyzerConfigOptions = null,
        params IIncrementalGenerator[] generators)
    {
        if (generators == null || generators.Length == 0)
        {
            throw new ArgumentException("At least one generator must be provided", nameof(generators));
        }

        // 1. Create external assembly
        var externalCompilation = CreateCompilation(
            externalAssemblySource,
            assemblyName: "ExternalAssembly");

        // 2. Create main assembly that references external assembly
        var mainCompilation = CreateCompilation(
            mainAssemblySource,
            assemblyName: "MainAssembly",
            externalCompilation.ToMetadataReference()
        );

        // 3. Create the driver
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators.Select(g => g.AsSourceGenerator()),
            parseOptions: (CSharpParseOptions)mainCompilation.SyntaxTrees.First().Options,
            optionsProvider: analyzerConfigOptions != null
                ? new DictionaryAnalyzerConfigOptionsProvider(analyzerConfigOptions)
                : null);

        // 4. Run generator on main assembly
        driver = driver.RunGenerators(mainCompilation);

        // 5. Verify
        return Verify(driver);
    }

    /// <summary>
    /// Helper to create a CSharpCompilation from source.
    /// </summary>
    private static CSharpCompilation CreateCompilation(
        string source,
        string assemblyName = "TestAssembly",
        params MetadataReference[] additionalReferences)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source, 
            new CSharpParseOptions(LanguageVersion.Latest));

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(INotifyPropertyChanged).Assembly.Location),
            // If you need ReactiveUI or other references, add them similarly:
            // MetadataReference.CreateFromFile(typeof(ReactiveObject).Assembly.Location),
        };
        references.AddRange(additionalReferences);

        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return compilation;
    }

    /// <summary>
    /// Reuses your existing verification approach.
    /// </summary>
    private static Task Verify(GeneratorDriver driver)
    {
        var runResults = driver.GetRunResult();

        // Gather generated sources
        var generatedSources = runResults.GeneratedTrees
            .Select(tree => new
            {
                FileName = Path.GetFileName(tree.FilePath),
                Source = tree.ToString()
            })
            .OrderBy(x => x.FileName)
            .ToList();

        // Optionally check if no sources were generated:
        // if (!generatedSources.Any())
        // {
        //     throw new Exception("No source was generated!");
        // }

        // Return the verification structure
        return Verifier.Verify(new
        {
            Sources = generatedSources,
            Diagnostics = runResults.Diagnostics
        });
    }
}

/// <summary>
/// Simple dictionary-based analyzer config.
/// </summary>
public class DictionaryAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
{
    private readonly DictionaryAnalyzerConfigOptions _options;

    public DictionaryAnalyzerConfigOptionsProvider(Dictionary<string, string> options)
    {
        _options = new DictionaryAnalyzerConfigOptions(options);
    }

    public override AnalyzerConfigOptions GlobalOptions => _options;

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _options;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _options;
}

public class DictionaryAnalyzerConfigOptions : AnalyzerConfigOptions
{
    private readonly Dictionary<string, string> _options;

    public DictionaryAnalyzerConfigOptions(Dictionary<string, string> options)
    {
        _options = options;
    }

    public override bool TryGetValue(string key, out string value)
        => _options.TryGetValue(key, out value!);
}
