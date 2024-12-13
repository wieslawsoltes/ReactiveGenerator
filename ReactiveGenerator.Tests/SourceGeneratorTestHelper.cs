using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ReactiveGenerator.Tests;

public static class SourceGeneratorTestHelper
{
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

        // Create compilation
        var compilation = CreateCompilation(source);
        
        // Create the driver with generators and options
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators.Select(g => g.AsSourceGenerator()),
            parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options,
            optionsProvider: analyzerConfigOptions != null 
                ? new DictionaryAnalyzerConfigOptionsProvider(analyzerConfigOptions) 
                : null);

        // Run generation
        driver = driver.RunGenerators(compilation);

        // Use our verify helper
        return Verify(driver);
    }

    private static Compilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(INotifyPropertyChanged).Assembly.Location),
            // Add ReactiveUI reference if needed for your tests
            // MetadataReference.CreateFromFile(typeof(ReactiveObject).Assembly.Location)
        };

        var compilation = CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return compilation;
    }

    private static Task Verify(GeneratorDriver driver)
    {
        var runResults = driver.GetRunResult();

        // Get the generated sources from the compilation
        var generatedSources = runResults.GeneratedTrees
            .Select(tree => new
            {
                FileName = Path.GetFileName(tree.FilePath),
                Source = tree.ToString()
            })
            .OrderBy(f => f.FileName)
            .ToList();

        if (!generatedSources.Any())
        {
            throw new Exception("No source was generated!");
        }

        // Return results in a verifiable format
        return Verifier.Verify(new
        {
            Sources = generatedSources,
            Diagnostics = runResults.Diagnostics
        });
    }
}

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
