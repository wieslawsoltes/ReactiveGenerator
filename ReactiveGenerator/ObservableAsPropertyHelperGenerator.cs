using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ReactiveGenerator;

[Generator]
public class ObservableAsPropertyHelperGenerator : IIncrementalGenerator
{
    private sealed record PropertyInfo
    {
        public IPropertySymbol Property { get; init; }
        public INamedTypeSymbol ContainingType { get; init; }
        public Location Location { get; init; }

        public PropertyInfo(IPropertySymbol property, INamedTypeSymbol containingType, Location location)
        {
            Property = property;
            ContainingType = containingType;
            Location = location;
        }

        public bool Equals(PropertyInfo? other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return SymbolEqualityComparer.Default.Equals(Property, other.Property) &&
                   SymbolEqualityComparer.Default.Equals(ContainingType, other.ContainingType);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 23 + (Property != null ? SymbolEqualityComparer.Default.GetHashCode(Property) : 0);
                hash = hash * 23 + (ContainingType != null
                    ? SymbolEqualityComparer.Default.GetHashCode(ContainingType)
                    : 0);
                return hash;
            }
        }
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Register attribute
        context.RegisterPostInitializationOutput(ctx =>
        {
            ctx.AddSource(
                "ObservableAsPropertyAttribute.g.cs",
                SourceText.From(ObservableAsPropertyAttributeSource, Encoding.UTF8));
        });

        IncrementalValuesProvider<PropertyInfo> propertyDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: (s, _) => IsCandidateProperty(s),
                transform: (ctx, _) => GetPropertyInfo(ctx))
            .Where(p => p is not null)
            .Select((p, _) => p!);

        var compilationAndProperties = context.CompilationProvider.Combine(propertyDeclarations.Collect());

        context.RegisterSourceOutput(
            compilationAndProperties,
            (spc, source) => Execute(
                source.Left,
                source.Right.ToList(),
                spc));
    }

    private static bool IsCandidateProperty(SyntaxNode node)
    {
        if (node is not PropertyDeclarationSyntax propertyDeclaration)
            return false;

        if (!propertyDeclaration.Modifiers.Any(m => m.ValueText == "partial"))
            return false;

        return propertyDeclaration.AttributeLists.Any(al =>
            al.Attributes.Any(a => a.Name.ToString() is "ObservableAsProperty" or "ObservableAsPropertyAttribute"));
    }

    private static PropertyInfo? GetPropertyInfo(GeneratorSyntaxContext context)
    {
        if (context.Node is not PropertyDeclarationSyntax propertyDeclaration)
            return null;

        var symbol = context.SemanticModel.GetDeclaredSymbol(propertyDeclaration);
        if (symbol is not IPropertySymbol propertySymbol)
            return null;

        var containingType = propertySymbol.ContainingType;
        if (containingType == null)
            return null;

        if (!ReactiveDetectionHelper.InheritsFromReactiveObject(containingType))
            return null;

        return new PropertyInfo(propertySymbol, containingType, propertyDeclaration.GetLocation());
    }

    private static void Execute(
        Compilation compilation,
        List<PropertyInfo> properties,
        SourceProductionContext context)
    {
        if (properties.Count == 0) return;

        var propertyGroups = properties
            .GroupBy<PropertyInfo, INamedTypeSymbol>(
                p => p.ContainingType,
                SymbolEqualityComparer.Default);

        foreach (var group in propertyGroups)
        {
            var typeSymbol = group.Key;
            if (!ReactiveDetectionHelper.IsTypeReactive(typeSymbol))
                continue;

            var source = GenerateHelperProperties(typeSymbol, group.ToList());

            var fullTypeName = typeSymbol.ToDisplayString(new SymbolDisplayFormat(
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                genericsOptions: SymbolDisplayGenericsOptions.None,
                miscellaneousOptions: SymbolDisplayMiscellaneousOptions.None));

            var fileName = $"{fullTypeName}.ObservableAsProperty.g.cs";
            context.AddSource(fileName, SourceText.From(source, Encoding.UTF8));
        }
    }

    private static string GenerateHelperProperties(
        INamedTypeSymbol classSymbol,
        List<PropertyInfo> properties)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using ReactiveUI;");
        sb.AppendLine();

        var namespaceName = classSymbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : classSymbol.ContainingNamespace.ToDisplayString();

        var containingTypes = GetContainingTypesChain(classSymbol).ToList();

        if (namespaceName != null)
        {
            sb.AppendLine($"namespace {namespaceName}");
            sb.AppendLine("{");
        }

        var indent = namespaceName != null ? "    " : "";

        foreach (var containingType in containingTypes)
        {
            var containingTypeAccessibility = containingType.DeclaredAccessibility.ToString().ToLowerInvariant();
            sb.AppendLine($"{indent}{containingTypeAccessibility} partial class {containingType.Name}");
            sb.AppendLine($"{indent}{{");
            indent += "    ";
        }

        var accessibility = classSymbol.DeclaredAccessibility.ToString().ToLowerInvariant();

        var typeParameters = "";
        var typeConstraints = "";
        if (classSymbol.TypeParameters.Length > 0)
        {
            typeParameters = "<" + string.Join(", ", classSymbol.TypeParameters.Select(tp => tp.Name)) + ">";
            typeConstraints = GenerateTypeConstraints(classSymbol.TypeParameters);
        }

        sb.AppendLine($"{indent}{accessibility} partial class {classSymbol.Name}{typeParameters}{typeConstraints}");
        sb.AppendLine($"{indent}{{");

        var lastProperty = properties.Last();
        foreach (var property in properties)
        {
            GenerateObservableAsPropertyHelper(sb, property.Property, indent + "    ");

            if (!SymbolEqualityComparer.Default.Equals(property.Property, lastProperty.Property))
            {
                sb.AppendLine();
            }
        }

        sb.AppendLine($"{indent}}}");

        for (int i = 0; i < containingTypes.Count; i++)
        {
            indent = indent.Substring(0, indent.Length - 4);
            sb.AppendLine($"{indent}}}");
        }

        if (namespaceName != null)
        {
            sb.AppendLine("}");
        }

        return sb.ToString();
    }

    private static string GenerateTypeConstraints(ImmutableArray<ITypeParameterSymbol> typeParameters)
    {
        var constraints = new List<string>();
        foreach (var typeParam in typeParameters)
        {
            var paramConstraints = new List<string>();

            if (typeParam.HasReferenceTypeConstraint)
                paramConstraints.Add("class");
            else if (typeParam.HasValueTypeConstraint)
                paramConstraints.Add("struct");

            foreach (var constraintType in typeParam.ConstraintTypes)
            {
                paramConstraints.Add(constraintType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            }

            if (typeParam.HasConstructorConstraint)
                paramConstraints.Add("new()");

            if (paramConstraints.Count > 0)
            {
                constraints.Add($"where {typeParam.Name} : {string.Join(", ", paramConstraints)}");
            }
        }

        return constraints.Count > 0 ? " " + string.Join(" ", constraints) : "";
    }

    private static void GenerateObservableAsPropertyHelper(StringBuilder sb, IPropertySymbol property, string indent)
    {
        var nullablePropertyType = GetPropertyTypeWithNullability(property);
        var accessibility = property.DeclaredAccessibility.ToString().ToLowerInvariant();
        var backingFieldName = $"_{char.ToLowerInvariant(property.Name[0])}{property.Name.Substring(1)}Helper";

        sb.AppendLine($"{indent}private ObservableAsPropertyHelper<{nullablePropertyType}> {backingFieldName};");
        sb.AppendLine();

        sb.AppendLine($"{indent}{accessibility} partial {nullablePropertyType} {property.Name}");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    get => {backingFieldName}.Value;");
        sb.AppendLine($"{indent}}}");
    }

    private static string GetPropertyTypeWithNullability(IPropertySymbol property)
    {
        var nullableAnnotation = property.NullableAnnotation;
        var baseType = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        if (nullableAnnotation == NullableAnnotation.Annotated && !property.Type.IsValueType)
        {
            return baseType + "?";
        }

        return baseType;
    }

    private static IEnumerable<INamedTypeSymbol> GetContainingTypesChain(INamedTypeSymbol symbol)
    {
        var types = new List<INamedTypeSymbol>();
        var current = symbol.ContainingType;
        while (current != null)
        {
            types.Insert(0, current);
            current = current.ContainingType;
        }

        return types;
    }

    private const string ObservableAsPropertyAttributeSource = @"// <auto-generated/>
#nullable enable

using System;

namespace ReactiveGenerator
{
    [AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
    public sealed class ObservableAsPropertyAttribute : Attribute
    {
        public ObservableAsPropertyAttribute() { }
    }
}";
}
