using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeActions;
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
        return TestAndVerifyWithFix(source, null, analyzerConfigOptions, testName, analyzers);
    }

    public static async Task TestAndVerifyWithFix(
        string source,
        string? equivalenceKey,
        Dictionary<string, string>? analyzerConfigOptions = null,
        [CallerMemberName] string? testName = null,
        params DiagnosticAnalyzer[] analyzers)
    {
        if (analyzers == null || analyzers.Length == 0)
        {
            throw new ArgumentException("At least one analyzer must be provided", nameof(analyzers));
        }

        // Create initial project with the source
        var project = CreateProject(source);
        var document = project.Documents.First();

        // Run analysis
        var compilation = await project.GetCompilationAsync();
        var compilationWithAnalyzers = compilation!
            .WithAnalyzers(ImmutableArray.Create(analyzers),
                new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty));

        // Get diagnostics synchronously to ensure consistency
        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();

        // Sort diagnostics deterministically
        diagnostics = diagnostics.OrderBy(d => d.Location.SourceSpan.Start)
            .ThenBy(d => d.Id)
            .ToImmutableArray();

        var newSource = source;
        if (diagnostics.Any())
        {
            var codeFixProvider = new ReactivePropertyCodeFixProvider();

            // Apply fixes one at a time in a deterministic order
            foreach (var diagnostic in diagnostics)
            {
                var actions = new List<CodeAction>();
                var context = new CodeFixContext(document, diagnostic,
                    (a, _) =>
                    {
                        if (equivalenceKey == null || a.EquivalenceKey == equivalenceKey)
                        {
                            actions.Add(a);
                        }
                    },
                    CancellationToken.None);

                await codeFixProvider.RegisterCodeFixesAsync(context);

                if (actions.Any())
                {
                    // Sort actions deterministically
                    actions = actions
                        .OrderBy(a => a.EquivalenceKey)
                        .ThenBy(a => a.Title)
                        .ToList();

                    var action = actions[0];

                    // Apply the fix
                    var operations = await action.GetOperationsAsync(CancellationToken.None);
                    var operation = operations.OfType<ApplyChangesOperation>().First();

                    // Get the new solution and document
                    project = operation.ChangedSolution.GetProject(project.Id)!;
                    document = project.Documents.First();

                    // Update the source for verification
                    newSource = (await document.GetTextAsync()).ToString();
                }
            }
        }

        // Verify results
        await Verifier.Verify(new
        {
            Diagnostics = diagnostics.Select(d => new
            {
                d.Id,
                d.Severity,
                Location = d.Location.GetLineSpan().StartLinePosition,
                Message = d.GetMessage()
            }).OrderBy(d => d.Location.Line).ThenBy(d => d.Id),
            FixedSource = newSource
        }).UseDirectory("Snapshots");
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
            MetadataReference.CreateFromFile(typeof(System.ComponentModel.INotifyPropertyChanged).Assembly
                .Location),
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
            MetadataReference.CreateFromFile(typeof(System.ComponentModel.INotifyPropertyChanged).Assembly
                .Location),
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
