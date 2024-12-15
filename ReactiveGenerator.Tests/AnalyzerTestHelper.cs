using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CodeFixes;
using System.Runtime.CompilerServices;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CodeActions;

namespace ReactiveGenerator.Tests;

public static class AnalyzerTestHelper
{
    public static async Task TestAndVerify(
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
        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();

        // Apply code fixes if available
        var codeFixProvider = new ReactivePropertyCodeFixProvider();
        var newSource = source;
        
        if (diagnostics.Any() && codeFixProvider != null)
        {
            var document = compilation.SyntaxTrees.First().GetText().ToString();
            var project = CreateProject(newSource);
            var documentId = project.DocumentIds[0];
            
            var fixedDocument = project.GetDocument(documentId);
            foreach (var diagnostic in diagnostics)
            {
                var actions = new List<CodeAction>();
                var context = new CodeFixContext(fixedDocument, diagnostic,
                    (a, d) => actions.Add(a), CancellationToken.None);
                
                await codeFixProvider.RegisterCodeFixesAsync(context);
                
                if (actions.Any())
                {
                    var operation = await actions[0].GetOperationsAsync(CancellationToken.None);
                    var solution = operation.OfType<ApplyChangesOperation>().Single().ChangedSolution;
                    fixedDocument = solution.GetDocument(documentId);
                }
            }
            
            newSource = (await fixedDocument.GetSyntaxRootAsync())?.ToFullString() ?? source;
        }

        // Use Verify
        await Verifier.Verify(new
        {
            Diagnostics = diagnostics.Select(d => new
            {
                d.Id,
                d.Severity,
                Location = d.Location.GetLineSpan().StartLinePosition,
                Message = d.GetMessage()
            }).OrderBy(d => d.Id).ThenBy(d => d.Location.Line),
            FixedSource = newSource
        });
    }

    private static Project CreateProject(string source)
    {
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);
        var solution = new AdhocWorkspace()
            .CurrentSolution
            .AddProject(projectId, "TestProject", "TestProject", LanguageNames.CSharp)
            .WithProjectCompilationOptions(projectId, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddDocument(documentId, "Test.cs", source);

        var project = solution.GetProject(projectId)!;

        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.ComponentModel.INotifyPropertyChanged).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ReactiveUI.ReactiveObject).Assembly.Location)
        };

        project = project.AddMetadataReferences(references);
        return project;
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
