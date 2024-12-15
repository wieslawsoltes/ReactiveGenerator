using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ReactiveGenerator;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class ReactivePropertyAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "REACTIVE001";
    private const string Title = "Property can use [Reactive] attribute";
    private const string MessageFormat = "Property '{0}' can be simplified using [Reactive] attribute";
    private const string Description = "Properties using RaiseAndSetIfChanged can be simplified using the [Reactive] attribute.";
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
