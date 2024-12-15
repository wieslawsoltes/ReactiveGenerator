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

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ReactivePropertyAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTIVE001";
    private const string Title = "Property can use [Reactive] attribute";
    private const string MessageFormat = "Property '{0}' can be simplified using [Reactive] attribute";

    private const string Description =
        "Properties using RaiseAndSetIfChanged can be simplified using the [Reactive] attribute.";

    private const string Category = "Usage";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeProperty, SyntaxKind.PropertyDeclaration);
    }

    private void AnalyzeProperty(SyntaxNodeAnalysisContext context)
    {
        var propertyDeclaration = (PropertyDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        // Check if property has both getter and setter
        if (!HasGetterAndSetter(propertyDeclaration))
            return;

        // Check if the setter uses RaiseAndSetIfChanged
        if (!UsesRaiseAndSetIfChanged(propertyDeclaration))
            return;

        // Check if containing type is ReactiveObject
        var containingType = semanticModel.GetDeclaredSymbol(propertyDeclaration)?.ContainingType;
        if (containingType == null || !InheritsFromReactiveObject(containingType))
            return;

        // Report diagnostic on the entire property declaration
        var diagnostic = Diagnostic.Create(
            Rule,
            propertyDeclaration.GetLocation(),
            propertyDeclaration.Identifier.Text);

        context.ReportDiagnostic(diagnostic);
    }

    private bool InheritsFromReactiveObject(INamedTypeSymbol typeSymbol)
    {
        var current = typeSymbol;
        while (current != null)
        {
            if (current.Name == "ReactiveObject")
                return true;
            current = current.BaseType;
        }

        return false;
    }

    private bool HasGetterAndSetter(PropertyDeclarationSyntax property)
    {
        if (property.AccessorList == null)
            return false;

        var accessors = property.AccessorList.Accessors;
        return accessors.Any(a => a.IsKind(SyntaxKind.GetAccessorDeclaration)) &&
               accessors.Any(a => a.IsKind(SyntaxKind.SetAccessorDeclaration));
    }

    private bool UsesRaiseAndSetIfChanged(PropertyDeclarationSyntax property)
    {
        var setter = property.AccessorList?.Accessors
            .FirstOrDefault(a => a.IsKind(SyntaxKind.SetAccessorDeclaration));

        if (setter?.Body == null && setter?.ExpressionBody == null)
            return false;

        // Get the setter's body text
        var setterText = setter.ToString();

        // Check for RaiseAndSetIfChanged pattern
        if (!setterText.Contains("RaiseAndSetIfChanged"))
            return false;

        // Additionally check for correct backing field reference
        var backingFieldName = "_" + char.ToLower(property.Identifier.Text[0]) +
                               property.Identifier.Text.Substring(1);
        return setterText.Contains($"ref {backingFieldName}");
    }
}

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

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;
        var propertyDeclaration = root.FindToken(diagnosticSpan.Start)
            .Parent?.AncestorsAndSelf()
            .OfType<PropertyDeclarationSyntax>()
            .First();

        if (propertyDeclaration == null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Convert to [Reactive] property",
                createChangedDocument: c => ConvertToReactivePropertyAsync(context.Document, propertyDeclaration, c),
                equivalenceKey: nameof(ReactivePropertyCodeFixProvider)),
            diagnostic);
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
                            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                        SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                    })))
            .WithLeadingTrivia(SyntaxFactory.Whitespace("    "));

        // Create new members list without backing field and with new property
        var newMembers = classDeclaration.Members
            .Where(member => !SymbolEqualityComparer.Default.Equals(
                semanticModel.GetDeclaredSymbol(member),
                semanticModel.GetDeclaredSymbol(backingField)))
            .Select(member =>
            {
                if (member == propertyDeclaration)
                    return newProperty;
                return member;
            });

        // Create new class declaration
        var newClass = classDeclaration
            .WithMembers(SyntaxFactory.List(newMembers));

        // Replace the class in the root
        var newRoot = root.ReplaceNode(classDeclaration, newClass);

        return document.WithSyntaxRoot(newRoot);
    }
}
