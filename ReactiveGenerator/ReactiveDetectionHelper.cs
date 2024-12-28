using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ReactiveGenerator;

/// <summary>
/// Helper class for detecting and analyzing reactive properties and types in source code.
/// </summary>
internal static class ReactiveDetectionHelper
{
    /// <summary>
    /// Determines if a class should be treated as reactive.
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <returns>True if the type should be treated as reactive.</returns>
    public static bool IsTypeReactive(INamedTypeSymbol type)
    {
        // If type inherits from ReactiveObject, it's already reactive
        if (InheritsFromReactiveObject(type))
            return true;

        // First check if the type has [IgnoreReactive]
        foreach (var attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass?.Name is "IgnoreReactiveAttribute" or "IgnoreReactive")
                return false;
        }

        // Then check if the type has [Reactive] (including base types)
        var current = type;
        while (current != null)
        {
            foreach (var attribute in current.GetAttributes())
            {
                if (attribute.AttributeClass?.Name is "ReactiveAttribute" or "Reactive")
                    return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    /// <summary>
    /// Determines if a specific property should be treated as reactive.
    /// </summary>
    /// <param name="property">The property to check.</param>
    /// <param name="isContainingTypeReactive">Whether the containing type is reactive.</param>
    /// <returns>True if the property should be treated as reactive.</returns>
    public static bool IsPropertyReactive(IPropertySymbol property, bool isContainingTypeReactive)
    {
        // Check for [IgnoreReactive] first - this takes precedence
        var hasIgnoreAttribute = property.GetAttributes()
            .Any(a => a.AttributeClass?.Name is "IgnoreReactiveAttribute" or "IgnoreReactive");
        
        if (hasIgnoreAttribute)
            return false;

        // Check for explicit [Reactive] attribute
        var hasReactiveAttribute = property.GetAttributes()
            .Any(a => a.AttributeClass?.Name is "ReactiveAttribute" or "Reactive");

        // Property is reactive if it has [Reactive] attribute or if containing type is reactive
        return hasReactiveAttribute || isContainingTypeReactive;
    }

    /// <summary>
    /// Checks if a type inherits from ReactiveObject.
    /// </summary>
    /// <param name="typeSymbol">The type to check.</param>
    /// <returns>True if the type inherits from ReactiveObject.</returns>
    public static bool InheritsFromReactiveObject(INamedTypeSymbol typeSymbol)
    {
        var current = typeSymbol;
        while (current is not null)
        {
            if (current.Name == "ReactiveObject" ||
                current.ToString() == "ReactiveUI.ReactiveObject" ||
                current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                "global::ReactiveUI.ReactiveObject")
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    /// <summary>
    /// Gets all reactive properties for a type.
    /// </summary>
    /// <param name="typeSymbol">The type to analyze.</param>
    /// <returns>An enumerable of reactive property symbols.</returns>
    public static IEnumerable<IPropertySymbol> GetReactiveProperties(INamedTypeSymbol typeSymbol)
    {
        var isReactiveClass = IsTypeReactive(typeSymbol);

        return typeSymbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => IsPropertyReactive(p, isReactiveClass));
    }

    /// <summary>
    /// Gets the depth of a type in its inheritance hierarchy.
    /// </summary>
    /// <param name="type">The type to analyze.</param>
    /// <returns>The depth in the inheritance hierarchy.</returns>
    public static int GetTypeHierarchyDepth(INamedTypeSymbol type)
    {
        int depth = 0;
        var current = type.BaseType;
        while (current != null)
        {
            depth++;
            current = current.BaseType;
        }
        return depth;
    }

    /// <summary>
    /// Checks if a type has any reactive attributes or properties.
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <returns>True if the type has reactive attributes or properties.</returns>
    public static bool HasReactiveAttributes(INamedTypeSymbol type)
    {
        return IsTypeReactive(type) || GetReactiveProperties(type).Any();
    }

    /// <summary>
    /// Analyzes a class declaration for reactive properties and attributes.
    /// </summary>
    /// <param name="context">The generator syntax context.</param>
    /// <param name="classDeclaration">The class declaration syntax.</param>
    /// <returns>A tuple containing the type symbol and location if the class is reactive, null otherwise.</returns>
    public static (INamedTypeSymbol Symbol, Location Location)? AnalyzeClassDeclaration(
        GeneratorSyntaxContext context,
        ClassDeclarationSyntax classDeclaration)
    {
        var symbol = (INamedTypeSymbol?)context.SemanticModel.GetDeclaredSymbol(classDeclaration);
        if (symbol == null)
            return null;

        if (HasReactiveAttributes(symbol))
        {
            return (symbol, classDeclaration.GetLocation());
        }

        return null;
    }

    /// <summary>
    /// Checks if a type is accessible (not private) throughout its containing type chain.
    /// </summary>
    /// <param name="typeSymbol">The type to check.</param>
    /// <returns>True if the type is accessible.</returns>
    public static bool IsTypeAccessible(INamedTypeSymbol typeSymbol)
    {
        var current = typeSymbol;
        while (current != null)
        {
            if (current.DeclaredAccessibility == Accessibility.Private)
                return false;
            current = current.ContainingType;
        }
        return true;
    }

    /// <summary>
    /// Gets the full type name including nullability annotations.
    /// </summary>
    /// <param name="property">The property whose type to analyze.</param>
    /// <returns>The full type name with nullability.</returns>
    public static string GetPropertyTypeWithNullability(IPropertySymbol property)
    {
        var nullableAnnotation = property.NullableAnnotation;
        var baseType = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        if (nullableAnnotation == NullableAnnotation.Annotated && !property.Type.IsValueType)
        {
            return baseType + "?";
        }

        return baseType;
    }

    /// <summary>
    /// Gets the accessibility level for a property accessor.
    /// </summary>
    /// <param name="accessor">The accessor method symbol.</param>
    /// <returns>The accessibility level as a string.</returns>
    public static string GetAccessorAccessibility(IMethodSymbol? accessor)
    {
        if (accessor is null)
            return "private";

        return accessor.DeclaredAccessibility switch
        {
            Accessibility.Private => "private",
            Accessibility.Protected => "protected",
            Accessibility.Internal => "internal",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            _ => accessor.DeclaredAccessibility.ToString().ToLowerInvariant()
        };
    }

    /// <summary>
    /// Formats a type name for use in XML documentation.
    /// </summary>
    /// <param name="type">The type to format.</param>
    /// <returns>The formatted type name.</returns>
    public static string FormatTypeNameForXmlDoc(ITypeSymbol type)
    {
        var format = new SymbolDisplayFormat(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

        return type.ToDisplayString(format).Replace("<", "{").Replace(">", "}");
    }

    /// <summary>
    /// Gets a collection of all types in a specified compilation.
    /// </summary>
    /// <param name="compilation">The compilation to analyze.</param>
    /// <returns>An enumerable of all named type symbols.</returns>
    public static IEnumerable<INamedTypeSymbol> GetAllTypesInCompilation(Compilation compilation)
    {
        var result = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        void ProcessNamespaceTypes(INamespaceSymbol ns)
        {
            foreach (var member in ns.GetMembers())
            {
                switch (member)
                {
                    case INamespaceSymbol nestedNs:
                        ProcessNamespaceTypes(nestedNs);
                        break;
                    case INamedTypeSymbol type:
                        result.Add(type);
                        foreach (var nestedType in type.GetTypeMembers())
                        {
                            result.Add(nestedType);
                        }
                        break;
                }
            }
        }

        ProcessNamespaceTypes(compilation.GlobalNamespace);
        return result;
    }
}
