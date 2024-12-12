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
        [CallerMemberName] string? testName = null)
    {
        // Create compilation
        var compilation = CreateCompilation(source);
        
        // Create generator instance
        var generator = new ReactiveGenerator();
        
        // Create the driver with generator and options
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() },
            parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options,
            optionsProvider: analyzerConfigOptions != null 
                ? new DictionaryAnalyzerConfigOptionsProvider(analyzerConfigOptions) 
                : null);

        // Run generation
        driver = driver.RunGenerators(compilation);

        // Use our verify helper
        return TestHelpers.Verify(driver);
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
