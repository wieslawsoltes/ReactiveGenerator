using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ReactiveGenerator;

[Generator]
public class ObservableAsPropertyHelperGenerator : IIncrementalGenerator
{
    private sealed record PropertyInfo
    {
        public IPropertySymbol Property { get; init; }
        public INamedTypeSymbol ContainingType { get; init; } // Ensure this is non-nullable
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

        // Find property declarations with [ObservableAsProperty] attribute
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

        // Must be partial
        if (!propertyDeclaration.Modifiers.Any(m => m.ValueText == "partial"))
            return false;

        // Must have [ObservableAsProperty] attribute
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

        return new PropertyInfo(propertySymbol, containingType, propertyDeclaration.GetLocation());
    }

    private static void Execute(
        Compilation compilation,
        List<PropertyInfo> properties,
        SourceProductionContext context)
    {
        if (properties.Count == 0) return;

        // Group properties by containing type with explicit type parameters
        var propertyGroups = properties
            .GroupBy<PropertyInfo, INamedTypeSymbol>(
                p => p.ContainingType,
                SymbolEqualityComparer.Default);

        foreach (var group in propertyGroups)
        {
            var typeSymbol = group.Key;
            var source = GenerateHelperProperties(typeSymbol, group.ToList());
            var fileName = $"{typeSymbol.Name}.ObservableAsProperty.g.cs";
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

        if (namespaceName != null)
        {
            sb.AppendLine($"namespace {namespaceName}");
            sb.AppendLine("{");
        }

        var accessibility = classSymbol.DeclaredAccessibility.ToString().ToLowerInvariant();

        // Add XML documentation comment if the class is public
        sb.AppendLine("    /// <summary>");
        sb.AppendLine(
            $"    /// A partial class implementation with observable property helpers for <see cref=\"{classSymbol.Name}\"/>.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine($"    {accessibility} partial class {classSymbol.Name}");
        sb.AppendLine("    {");

        // Generate backing fields and properties
        var lastProperty = properties.Last();
        foreach (var property in properties)
        {
            GenerateObservableAsPropertyHelper(sb, property.Property);

            if (!SymbolEqualityComparer.Default.Equals(property.Property, lastProperty.Property))
            {
                sb.AppendLine();
            }
        }

        sb.AppendLine("    }");

        if (namespaceName != null)
        {
            sb.AppendLine("}");
        }

        return sb.ToString();
    }

    private static void GenerateObservableAsPropertyHelper(StringBuilder sb, IPropertySymbol property)
    {
        var propertyType = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var nullablePropertyType = property.Type.NullableAnnotation == NullableAnnotation.NotAnnotated
            ? propertyType
            : $"{propertyType}?";
        var accessibility = property.DeclaredAccessibility.ToString().ToLowerInvariant();
        var backingFieldName = $"_{char.ToLowerInvariant(property.Name[0])}{property.Name.Substring(1)}Helper";

        // Add XML documentation for the backing field
        sb.AppendLine("        /// <summary>");
        sb.AppendLine($"        /// Gets the observable as property helper for <see cref=\"{property.Name}\"/>.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine(
            $"        private readonly ObservableAsPropertyHelper<{nullablePropertyType}> {backingFieldName};");
        sb.AppendLine();

        // Add XML documentation for the property
        sb.AppendLine("        /// <summary>");
        sb.AppendLine($"        /// Gets the value of the {property.Name} property.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine($"        /// <value>The value from the observable sequence.</value>");
        sb.AppendLine($"        {accessibility} partial {nullablePropertyType} {property.Name}");
        sb.AppendLine("        {");
        sb.AppendLine($"            get => {backingFieldName}.Value;");
        sb.AppendLine("        }");
    }

    private const string ObservableAsPropertyAttributeSource = @"using System;

namespace ReactiveGenerator
{
    /// <summary>
    /// Indicates that a property should be implemented as an ObservableAsPropertyHelper.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
    public sealed class ObservableAsPropertyAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref=""ObservableAsPropertyAttribute""/> class.
        /// </summary>
        public ObservableAsPropertyAttribute() { }
    }
}";
}
