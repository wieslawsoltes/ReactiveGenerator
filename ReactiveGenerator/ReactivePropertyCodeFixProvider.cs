using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using ReactiveGenerator;

[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public class ReactivePropertyCodeFixProvider : CodeFixProvider
{
    public sealed override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(ReactivePropertyAnalyzer.DiagnosticId);

    public sealed override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null) return;

        // Process each diagnostic
        foreach (var diagnostic in context.Diagnostics)
        {
            // Get all properties in the syntax tree
            var allProperties = root.DescendantNodes()
                .OfType<PropertyDeclarationSyntax>();

            // Find property whose span intersects with the diagnostic
            var diagnosticSpan = diagnostic.Location.SourceSpan;
            var propertyNode = allProperties.FirstOrDefault(p => 
                p.Span.IntersectsWith(diagnosticSpan) || 
                p.Identifier.Span.IntersectsWith(diagnosticSpan));

            // If we didn't find a property by span intersection, try to find by containment
            if (propertyNode == null)
            {
                propertyNode = allProperties.FirstOrDefault(p => 
                    p.FullSpan.Contains(diagnosticSpan.Start) || 
                    p.FullSpan.Contains(diagnosticSpan.End));
            }

            if (propertyNode == null) continue;

            // Register code fix
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Convert to [Reactive] property",
                    createChangedDocument: c => ConvertToReactivePropertyAsync(context.Document, propertyNode, c),
                    equivalenceKey: nameof(ReactivePropertyCodeFixProvider)),
                diagnostic);
        }
    }

    private async Task<Document> ConvertToReactivePropertyAsync(
        Document document,
        PropertyDeclarationSyntax propertyDeclaration,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
        if (root == null || semanticModel == null) return document;

        var classDeclaration = propertyDeclaration.Parent as ClassDeclarationSyntax;
        if (classDeclaration == null) return document;

        // Find the backing field name
        var backingFieldName = "_" + char.ToLower(propertyDeclaration.Identifier.Text[0]) +
                              propertyDeclaration.Identifier.Text.Substring(1);

        // Find the backing field
        var backingField = classDeclaration.Members
            .OfType<FieldDeclarationSyntax>()
            .FirstOrDefault(f => f.Declaration.Variables
                .Any(v => v.Identifier.Text == backingFieldName));

        // Create new reactive property with preserved modifiers
        var newProperty = SyntaxFactory.PropertyDeclaration(
                propertyDeclaration.Type,
                propertyDeclaration.Identifier)
            .WithAttributeLists(
                SyntaxFactory.SingletonList(
                    SyntaxFactory.AttributeList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.Attribute(SyntaxFactory.IdentifierName("Reactive"))))))
            .WithModifiers(
                propertyDeclaration.Modifiers
                    .Add(SyntaxFactory.Token(SyntaxKind.PartialKeyword)))
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
            .WithLeadingTrivia(propertyDeclaration.GetLeadingTrivia());

        // Create new class members list
        var newMembers = new List<MemberDeclarationSyntax>();
        foreach (var member in classDeclaration.Members)
        {
            if (member == propertyDeclaration)
            {
                newMembers.Add(newProperty);
                continue;
            }
            
            // Only remove backing field if not used elsewhere
            if (backingField != null && member == backingField)
            {
                var fieldSymbol = ModelExtensions.GetDeclaredSymbol(semanticModel, backingField.Declaration.Variables.First());
                if (fieldSymbol != null)
                {
                    var references = await SymbolFinder.FindReferencesAsync(
                        fieldSymbol,
                        document.Project.Solution,
                        cancellationToken);

                    var isUsedElsewhere = references.Any(r => r.Locations.Any(loc => 
                        !loc.Location.SourceSpan.IntersectsWith(propertyDeclaration.Span)));

                    if (!isUsedElsewhere)
                    {
                        continue;
                    }
                }
            }
            
            newMembers.Add(member);
        }

        // Replace nodes
        var newRoot = root.ReplaceNode(
            classDeclaration,
            classDeclaration.WithMembers(SyntaxFactory.List(newMembers)));

        return document.WithSyntaxRoot(newRoot);
    }
}
