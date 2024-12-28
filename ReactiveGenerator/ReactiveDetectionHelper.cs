using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ReactiveGenerator;

internal static class ReactiveDetectionHelper
{
    /// <summary>
    /// Determines if a class should be treated as reactive.
    /// </summary>
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
    public static IEnumerable<IPropertySymbol> GetReactiveProperties(INamedTypeSymbol typeSymbol)
    {
        var isReactiveClass = IsTypeReactive(typeSymbol);

        return typeSymbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => IsPropertyReactive(p, isReactiveClass));
    }
}
