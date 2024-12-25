using System.Collections.Immutable;
using System.Collections.Generic;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ReactiveGenerator;

[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public class ReactivePropertyCodeFixProvider : CodeFixProvider
{
    private const string SingleFixTitle = "Convert to [Reactive] property";
    private const string DocumentFixTitle = "Convert all properties to [Reactive] in file";
    private const string ProjectFixTitle = "Convert all properties to [Reactive] in project";
    private const string SolutionFixTitle = "Convert all properties to [Reactive] in solution";

    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(ReactivePropertyAnalyzer.DiagnosticId);

    private class CustomFixAllProvider : FixAllProvider
    {
        public static readonly CustomFixAllProvider Instance = new CustomFixAllProvider();

        public override async Task<CodeAction?> GetFixAsync(FixAllContext fixAllContext)
        {
            switch (fixAllContext.Scope)
            {
                case FixAllScope.Document:
                    var documentDiagnostics = await fixAllContext.GetDocumentDiagnosticsAsync(fixAllContext.Document);
                    if (!documentDiagnostics.Any()) return null;

                    return CodeAction.Create(
                        DocumentFixTitle,
                        c => GetFixedDocumentAsync(fixAllContext.Solution, fixAllContext.Document, documentDiagnostics,
                            c),
                        $"{nameof(ReactivePropertyCodeFixProvider)}_Document");

                case FixAllScope.Project:
                    var projectDiagnostics = await fixAllContext.GetAllDiagnosticsAsync(fixAllContext.Project);
                    if (!projectDiagnostics.Any()) return null;

                    return CodeAction.Create(
                        ProjectFixTitle,
                        c => GetFixedProjectAsync(fixAllContext.Project, c),
                        $"{nameof(ReactivePropertyCodeFixProvider)}_Project");

                case FixAllScope.Solution:
                    // Check if any project has diagnostics
                    foreach (var project in fixAllContext.Solution.Projects)
                    {
                        var solutionScopeDiagnostics = await fixAllContext.GetAllDiagnosticsAsync(project);
                        if (solutionScopeDiagnostics.Any())
                        {
                            return CodeAction.Create(
                                SolutionFixTitle,
                                c => GetFixedSolutionAsync(fixAllContext.Solution, c),
                                $"{nameof(ReactivePropertyCodeFixProvider)}_Solution");
                        }
                    }

                    return null;

                default:
                    return null;
            }
        }
    }

    public sealed override FixAllProvider GetFixAllProvider() => CustomFixAllProvider.Instance;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null) return;

        // Order diagnostics deterministically
        var diagnostics = context.Diagnostics
            .OrderBy(d => d.Location.SourceSpan.Start)
            .ToList();

        var diagnostic = diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        // Find all property declarations in a deterministic order
        var propertyNodes = root.DescendantNodes()
            .OfType<PropertyDeclarationSyntax>()
            .Where(p => p.Span.Contains(diagnosticSpan))
            .OrderBy(p => p.SpanStart)
            .ToList();

        if (!propertyNodes.Any()) return;

        var propertyNode = propertyNodes.First();

        // Register fixes in a deterministic order
        var fixes = new[]
        {
            (SingleFixTitle, "Single"), (DocumentFixTitle, "Document"), (ProjectFixTitle, "Project"),
            (SolutionFixTitle, "Solution")
        };

        foreach (var (title, scope) in fixes.OrderBy(f => f.Item1))
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: title,
                    createChangedSolution: c => GetFixedSolutionForScope(
                        context.Document,
                        propertyNode,
                        scope,
                        c),
                    equivalenceKey: $"{nameof(ReactivePropertyCodeFixProvider)}_{scope}"),
                diagnostic);
        }
    }

    private async Task<Solution> GetFixedSolutionForScope(
        Document document,
        PropertyDeclarationSyntax property,
        string scope,
        CancellationToken cancellationToken)
    {
        switch (scope)
        {
            case "Single":
                return (await ConvertToReactivePropertyAsync(document, property, cancellationToken))
                    .Project.Solution;

            case "Document":
                // Get all properties from the document in one go
                var root = await document.GetSyntaxRootAsync(cancellationToken);
                if (root == null) return document.Project.Solution;

                var nodesToReplace = new Dictionary<SyntaxNode, SyntaxNode>();
                var classModifications = new Dictionary<ClassDeclarationSyntax, SyntaxNode>();

                // Identify all convertible properties
                foreach (var prop in root.DescendantNodes()
                             .OfType<PropertyDeclarationSyntax>()
                             .Where(CanConvertToReactiveProperty)
                             .OrderBy(p => p.SpanStart))
                {
                    var classDeclaration = prop.Parent as ClassDeclarationSyntax;
                    if (classDeclaration == null) continue;

                    // Find backing field
                    var backingFieldName = "_" + char.ToLower(prop.Identifier.Text[0]) +
                                           prop.Identifier.Text.Substring(1);

                    var backingField = classDeclaration.Members
                        .OfType<FieldDeclarationSyntax>()
                        .FirstOrDefault(f => f.Declaration.Variables
                            .Any(v => v.Identifier.Text == backingFieldName));

                    // Create new reactive property
                    var newProperty = CreateReactiveProperty(prop);

                    // Track nodes to be replaced
                    nodesToReplace[prop] = newProperty;
                    if (backingField != null)
                    {
                        nodesToReplace[backingField] = null; // Mark for removal
                    }

                    // Ensure class is partial if not already
                    if (!classDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
                    {
                        var newClass = classDeclaration.WithModifiers(
                            classDeclaration.Modifiers.Add(SyntaxFactory.Token(SyntaxKind.PartialKeyword)));
                        classModifications[classDeclaration] = newClass;
                    }
                }

                // Apply all transformations at once
                var newRoot = root.ReplaceSyntax(
                    nodes: nodesToReplace.Keys,
                    computeReplacementNode: (oldNode, rewrittenOldNode) =>
                    {
                        if (nodesToReplace.TryGetValue(oldNode, out var replacement))
                        {
                            return replacement;
                        }

                        if (classModifications.TryGetValue(oldNode as ClassDeclarationSyntax, out var modifiedClass))
                        {
                            return modifiedClass;
                        }

                        return rewrittenOldNode;
                    },
                    tokens: null,
                    computeReplacementToken: null,
                    trivia: null,
                    computeReplacementTrivia: null);

                var newDocument = document.WithSyntaxRoot(newRoot);
                return newDocument.Project.Solution;

            case "Project":
                // Start with current solution
                var projectSolution = document.Project.Solution;
                var project = document.Project;

                // Process each document in the project
                foreach (var doc in project.Documents)
                {
                    var docRoot = await doc.GetSyntaxRootAsync(cancellationToken);
                    if (docRoot == null) continue;

                    // Get all properties in document
                    var projectProperties = docRoot.DescendantNodes()
                        .OfType<PropertyDeclarationSyntax>()
                        .Where(CanConvertToReactiveProperty)
                        .OrderBy(p => p.SpanStart)
                        .ToList();

                    // Convert properties
                    if (projectProperties.Any())
                    {
                        var currentDoc = projectSolution.GetDocument(doc.Id);
                        if (currentDoc == null) continue;

                        var documentSolution = await GetFixedSolutionForScope(
                            currentDoc,
                            projectProperties.First(),
                            "Document",
                            cancellationToken);

                        projectSolution = documentSolution;
                    }
                }

                return projectSolution;

            case "Solution":
                var solutionToFix = document.Project.Solution;

                // Process each project
                foreach (var proj in solutionToFix.Projects)
                {
                    var firstDoc = proj.Documents.FirstOrDefault();
                    if (firstDoc == null) continue;

                    // Get first doc with convertible properties
                    foreach (var doc in proj.Documents)
                    {
                        var docRoot = await doc.GetSyntaxRootAsync(cancellationToken);
                        if (docRoot == null) continue;

                        var hasConvertibleProperties = docRoot.DescendantNodes()
                            .OfType<PropertyDeclarationSyntax>()
                            .Any(CanConvertToReactiveProperty);

                        if (hasConvertibleProperties)
                        {
                            var updatedSolution = await GetFixedSolutionForScope(
                                doc,
                                property,
                                "Project",
                                cancellationToken);

                            solutionToFix = updatedSolution;
                            break;
                        }
                    }
                }

                return solutionToFix;

            default:
                return document.Project.Solution;
        }
    }

    private static bool CanConvertToReactiveProperty(PropertyDeclarationSyntax property)
    {
        // Must have both getter and setter
        if (property.AccessorList?.Accessors.Count != 2) return false;

        // Must have a set accessor with a body or expression
        var setAccessor = property.AccessorList.Accessors
            .FirstOrDefault(a => a.IsKind(SyntaxKind.SetAccessorDeclaration));
        if (setAccessor == null || setAccessor.Body == null && setAccessor.ExpressionBody == null) return false;

        // Check if the setter uses RaiseAndSetIfChanged
        var setterText = (setAccessor.Body?.ToString() ?? setAccessor.ExpressionBody?.ToString() ?? "")
            .Replace(" ", "").Replace("\n", "").Replace("\r", "");

        return setterText.Contains("this.RaiseAndSetIfChanged(ref");
    }

    private static async Task<Document> GetFixedDocumentAsync(Solution solution, Document document,
        ImmutableArray<Diagnostic> diagnostics, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        var propertyNodes = new List<PropertyDeclarationSyntax>();
        foreach (var diagnostic in diagnostics)
        {
            var span = diagnostic.Location.SourceSpan;
            var property = root.FindToken(span.Start)
                .Parent?
                .AncestorsAndSelf()
                .OfType<PropertyDeclarationSyntax>()
                .FirstOrDefault();

            if (property != null && CanConvertToReactiveProperty(property))
            {
                propertyNodes.Add(property);
            }
        }

        // Handle all properties as a single syntax tree transformation
        var nodesToReplace = new Dictionary<SyntaxNode, SyntaxNode>();
        var classModifications = new Dictionary<ClassDeclarationSyntax, SyntaxNode>();

        foreach (var property in propertyNodes)
        {
            var classDeclaration = property.Parent as ClassDeclarationSyntax;
            if (classDeclaration == null) continue;

            // Find the backing field name
            var backingFieldName = "_" + char.ToLower(property.Identifier.Text[0]) +
                                   property.Identifier.Text.Substring(1);

            // Find the backing field
            var backingField = classDeclaration.Members
                .OfType<FieldDeclarationSyntax>()
                .FirstOrDefault(f => f.Declaration.Variables
                    .Any(v => v.Identifier.Text == backingFieldName));

            // Create new reactive property
            var newProperty = CreateReactiveProperty(property);

            // Track nodes to be replaced
            nodesToReplace[property] = newProperty;
            if (backingField != null)
            {
                nodesToReplace[backingField] = null; // Mark for removal
            }

            // Ensure class is partial
            if (!classDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
            {
                var newClass = classDeclaration.WithModifiers(
                    classDeclaration.Modifiers.Add(SyntaxFactory.Token(SyntaxKind.PartialKeyword)));
                classModifications[classDeclaration] = newClass;
            }
        }

        // Apply all transformations at once
        var newRoot = root.ReplaceSyntax(
            nodes: nodesToReplace.Keys,
            computeReplacementNode: (oldNode, rewrittenOldNode) =>
            {
                if (nodesToReplace.TryGetValue(oldNode, out var replacement))
                {
                    return replacement;
                }

                if (classModifications.TryGetValue(oldNode as ClassDeclarationSyntax, out var modifiedClass))
                {
                    return modifiedClass;
                }

                return rewrittenOldNode;
            },
            tokens: null,
            computeReplacementToken: null,
            trivia: null,
            computeReplacementTrivia: null);

        return document.WithSyntaxRoot(newRoot);
    }

    private static PropertyDeclarationSyntax CreateReactiveProperty(PropertyDeclarationSyntax property)
    {
        // Get the full indentation from the property
        var leadingTrivia = property.GetLeadingTrivia();
        var indentation = SyntaxFactory.Whitespace(new string(' ', leadingTrivia.ToFullString().Count(c => c == ' ')));

        // Create the [Reactive] attribute with proper indentation
        var attributeList = SyntaxFactory.AttributeList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Attribute(SyntaxFactory.IdentifierName("Reactive"))))
            .WithLeadingTrivia(indentation);

        // Create new reactive property with proper indentation
        return SyntaxFactory.PropertyDeclaration(
                property.Type,
                property.Identifier)
            .WithAttributeLists(
                SyntaxFactory.SingletonList(attributeList))
            .WithModifiers(
                property.Modifiers.Add(SyntaxFactory.Token(SyntaxKind.PartialKeyword)))
            .WithAccessorList(
                SyntaxFactory.AccessorList(
                    SyntaxFactory.List(new[]
                    {
                        SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                            .WithModifiers(property.AccessorList?.Accessors
                                .FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration))?.Modifiers ?? default)
                            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                            .WithLeadingTrivia(indentation),
                        SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                            .WithModifiers(property.AccessorList?.Accessors
                                .FirstOrDefault(a => a.IsKind(SyntaxKind.SetAccessorDeclaration))?.Modifiers ?? default)
                            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                            .WithLeadingTrivia(indentation)
                    })))
            .WithLeadingTrivia(
                leadingTrivia.Any(t => t.IsKind(SyntaxKind.EndOfLineTrivia))
                    ? leadingTrivia
                    : leadingTrivia.Add(SyntaxFactory.EndOfLine("\n")).Add(indentation));
    }

    private static async Task<Solution> GetFixedProjectAsync(Project project, CancellationToken cancellationToken)
    {
        // Create analyzer and get all diagnostics in the project
        var analyzer = new ReactivePropertyAnalyzer();
        var compilation = await project.GetCompilationAsync(cancellationToken);
        if (compilation == null) return project.Solution;

        var diagnostics = await GetProjectDiagnosticsAsync(compilation, analyzer, project, cancellationToken);

        var solution = project.Solution;
        foreach (var documentGroup in diagnostics.GroupBy(d => d.Location.SourceTree))
        {
            var document = solution.GetDocument(documentGroup.Key);
            if (document == null) continue;

            var newDocument = await GetFixedDocumentAsync(solution, document, documentGroup.ToImmutableArray(),
                cancellationToken);
            solution = newDocument.Project.Solution;
        }

        return solution;
    }

    private static async Task<Solution> GetFixedSolutionAsync(Solution solution, CancellationToken cancellationToken)
    {
        foreach (var project in solution.Projects)
        {
            solution = await GetFixedProjectAsync(project, cancellationToken);
        }

        return solution;
    }

    private static async Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(
        Compilation compilation,
        DiagnosticAnalyzer analyzer,
        Project project,
        CancellationToken cancellationToken)
    {
        var compilationWithAnalyzer = compilation.WithAnalyzers(
            ImmutableArray.Create(analyzer),
            project.AnalyzerOptions,
            cancellationToken);

        var diagnostics = await compilationWithAnalyzer.GetAnalyzerDiagnosticsAsync(cancellationToken);
        return diagnostics.Where(d => d.Id == ReactivePropertyAnalyzer.DiagnosticId);
    }

    private static async Task<Document> ConvertToReactivePropertyAsync(
        Document document,
        PropertyDeclarationSyntax propertyDeclaration,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        var newRoot = await ConvertPropertyInRootAsync(root, propertyDeclaration, cancellationToken);
        return document.WithSyntaxRoot(newRoot);
    }

    private static async Task<SyntaxNode> ConvertPropertyInRootAsync(
        SyntaxNode root,
        PropertyDeclarationSyntax propertyDeclaration,
        CancellationToken cancellationToken)
    {
        var classDeclaration = propertyDeclaration.Parent as ClassDeclarationSyntax;
        if (classDeclaration == null) return root;

        // Find the backing field name
        var backingFieldName = "_" + char.ToLower(propertyDeclaration.Identifier.Text[0]) +
                               propertyDeclaration.Identifier.Text.Substring(1);

        // Find the backing field
        var backingField = classDeclaration.Members
            .OfType<FieldDeclarationSyntax>()
            .FirstOrDefault(f => f.Declaration.Variables
                .Any(v => v.Identifier.Text == backingFieldName));

        // Get the indentation from the property
        var leadingTrivia = propertyDeclaration.GetLeadingTrivia();
        var indentation = leadingTrivia
            .Where(t => t.IsKind(SyntaxKind.WhitespaceTrivia))
            .LastOrDefault();

        // Create the [Reactive] attribute with proper indentation
        var reactiveAttribute = SyntaxFactory.AttributeList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Attribute(SyntaxFactory.IdentifierName("Reactive"))))
            .WithLeadingTrivia(leadingTrivia);

        // Create new reactive property
        var newProperty = SyntaxFactory.PropertyDeclaration(
                propertyDeclaration.Type,
                propertyDeclaration.Identifier)
            .WithAttributeLists(
                SyntaxFactory.SingletonList(reactiveAttribute))
            .WithModifiers(
                propertyDeclaration.Modifiers.Add(SyntaxFactory.Token(SyntaxKind.PartialKeyword)))
            .WithAccessorList(
                SyntaxFactory.AccessorList(
                    SyntaxFactory.List(new[]
                    {
                        SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                            .WithModifiers(propertyDeclaration.AccessorList?.Accessors
                                .FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration))?.Modifiers ?? default)
                            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                        SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                            .WithModifiers(propertyDeclaration.AccessorList?.Accessors
                                .FirstOrDefault(a => a.IsKind(SyntaxKind.SetAccessorDeclaration))?.Modifiers ?? default)
                            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                    })))
            .WithLeadingTrivia(SyntaxFactory.LineFeed, indentation);

        // Build the new members list
        var newMembers = classDeclaration.Members
            .Select(member =>
            {
                if (member == propertyDeclaration)
                    return newProperty;
                if (member == backingField)
                    return null;
                return member;
            })
            .Where(member => member != null)
            .ToList();

        // Check if class already has partial modifier
        bool hasPartialModifier = classDeclaration.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));

        // If not partial, add the partial modifier
        var newClass = !hasPartialModifier
            ? classDeclaration
                .WithModifiers(classDeclaration.Modifiers.Add(SyntaxFactory.Token(SyntaxKind.PartialKeyword)))
                .WithMembers(SyntaxFactory.List(newMembers))
            : classDeclaration
                .WithMembers(SyntaxFactory.List(newMembers));

        return root.ReplaceNode(classDeclaration, newClass);
    }
}
