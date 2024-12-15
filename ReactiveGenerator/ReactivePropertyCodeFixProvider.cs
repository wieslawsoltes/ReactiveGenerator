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

        // Handle each diagnostic individually
        foreach (var diagnostic in context.Diagnostics)
        {
            var diagnosticSpan = diagnostic.Location.SourceSpan;
            
            // Find the property declaration identified by the diagnostic
            var propertyDeclaration = root
                .FindNode(diagnosticSpan)
                ?.AncestorsAndSelf()
                .OfType<PropertyDeclarationSyntax>()
                .FirstOrDefault();

            if (propertyDeclaration == null) continue;

            // Register the code fix
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Convert to [Reactive] property",
                    createChangedDocument: c => ConvertToReactivePropertyAsync(context.Document, propertyDeclaration, c),
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

        // Preserve accessor modifiers if they exist
        var getAccessor = propertyDeclaration.AccessorList?.Accessors
            .FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));
        var setAccessor = propertyDeclaration.AccessorList?.Accessors
            .FirstOrDefault(a => a.IsKind(SyntaxKind.SetAccessorDeclaration));

        // Create new reactive property
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
                            .WithModifiers(getAccessor?.Modifiers ?? default)
                            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                        SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                            .WithModifiers(setAccessor?.Modifiers ?? default)
                            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                    })))
            .WithLeadingTrivia(propertyDeclaration.GetLeadingTrivia());

        // Create new members list
        var newMembers = new List<MemberDeclarationSyntax>();
        foreach (var member in classDeclaration.Members)
        {
            if (member == propertyDeclaration)
            {
                newMembers.Add(newProperty);
                continue;
            }
            
            // Skip the backing field if it's not used elsewhere
            if (backingField != null && member == backingField)
            {
                // Check if backing field is used in other members
                var references = await SymbolFinder.FindReferencesAsync(
                    ModelExtensions.GetDeclaredSymbol(semanticModel, backingField.Declaration.Variables.First()),
                    document.Project.Solution,
                    cancellationToken);

                var isUsedElsewhere = references.Any(r => r.Locations.Any(loc => 
                    !loc.Location.SourceSpan.IntersectsWith(propertyDeclaration.Span)));

                if (!isUsedElsewhere)
                {
                    continue;
                }
            }
            
            newMembers.Add(member);
        }

        // Create new class declaration
        var newClass = classDeclaration
            .WithMembers(SyntaxFactory.List(newMembers));

        // Replace the class in the root
        var newRoot = root.ReplaceNode(classDeclaration, newClass);

        return document.WithSyntaxRoot(newRoot);
    }
}
