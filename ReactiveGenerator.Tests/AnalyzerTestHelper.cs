using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Runtime.CompilerServices;
using System.Collections.Immutable;

namespace ReactiveGenerator.Tests;

public static class AnalyzerTestHelper
{
    public static Task TestAndVerify(
        string source,
        Dictionary<string, string>? analyzerConfigOptions = null,
        [CallerMemberName] string? testName = null,
        params DiagnosticAnalyzer[] analyzers)
    {
        if (analyzers == null || analyzers.Length == 0)
        {
            throw new ArgumentException("At least one analyzer must be provided", nameof(analyzers));
        }

        // Create compilation
        var compilation = CreateCompilation(source);
        
        // Create analyzer driver
        var compilationWithAnalyzers = compilation
            .WithAnalyzers(ImmutableArray.Create(analyzers), 
                new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty));

        // Run analysis
        var diagnostics = compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync().Result;

        // Get any suggested fixes
        var fixes = new List<object>();
        // Note: You would need to implement code fix collection here if needed

        // Use Verify
        return Verifier.Verify(new
        {
            Diagnostics = diagnostics.Select(d => new
            {
                d.Id,
                d.Severity,
                d.Location.GetLineSpan().StartLinePosition,
                GetMessage = d.GetMessage()
            }).OrderBy(d => d.Id).ThenBy(d => d.StartLinePosition.Line),
            Fixes = fixes
        });
    }

    private static Compilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.ComponentModel.INotifyPropertyChanged).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ReactiveUI.ReactiveObject).Assembly.Location)
        };

        var compilation = CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return compilation;
    }
}
