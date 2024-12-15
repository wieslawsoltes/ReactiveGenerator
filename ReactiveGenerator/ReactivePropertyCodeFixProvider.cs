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
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FindSymbols;

namespace ReactiveGenerator;

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

        foreach (var diagnostic in context.Diagnostics)
        {
            var diagnosticSpan = diagnostic.Location.SourceSpan;
        
            // Find the property declaration containing the diagnostic span
            var propertyNode = root.DescendantNodes()
                .OfType<PropertyDeclarationSyntax>()
                .FirstOrDefault(p => p.Span.Contains(diagnosticSpan));
            
            if (propertyNode == null) continue;

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
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
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

        var newRoot = root.ReplaceNode(classDeclaration, newClass);

        return document.WithSyntaxRoot(newRoot);
    }
}
