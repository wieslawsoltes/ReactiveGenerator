using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ReactiveGenerator;

[Generator]
public class ReactiveGenerator : IIncrementalGenerator
{
    private record PropertyInfo(
        IPropertySymbol Property,
        bool HasReactiveAttribute,
        bool HasIgnoreAttribute,
        bool HasImplementation);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Register both attributes
        context.RegisterPostInitializationOutput(ctx =>
        {
            ctx.AddSource("ReactiveAttribute.g.cs", SourceText.From(AttributeSource, Encoding.UTF8));
            ctx.AddSource("IgnoreReactiveAttribute.g.cs", SourceText.From(IgnoreAttributeSource, Encoding.UTF8));
        });

        // Get MSBuild property for enabling legacy mode
        var useLegacyMode = context.AnalyzerConfigOptionsProvider
            .Select((provider, _) => bool.TryParse(
                provider.GlobalOptions.TryGetValue("build_property.UseBackingFields", out var value)
                    ? value
                    : "false",
                out var result) && result);

        // Get partial class declarations
        var partialClasses = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: (s, _) => s is ClassDeclarationSyntax c &&
                                     c.Modifiers.Any(m => m.ValueText == "partial") &&
                                     c.AttributeLists.Count > 0,
                transform: (ctx, _) => GetClassInfo(ctx))
            .Where(m => m is not null);

        // Get partial properties (both with [Reactive] and from [Reactive] classes)
        var partialProperties = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: (s, _) => s is PropertyDeclarationSyntax p &&
                                     p.Modifiers.Any(m => m.ValueText == "partial"),
                transform: (ctx, _) => GetPropertyInfo(ctx))
            .Where(m => m is not null);

        // Combine compilation, declarations, and configuration
        var compilationAndData = context.CompilationProvider
            .Combine(partialClasses.Collect())
            .Combine(partialProperties.Collect())
            .Combine(useLegacyMode);

        context.RegisterSourceOutput(
            compilationAndData,
            (spc, source) => Execute(
                source.Left.Left.Left,
                source.Left.Left.Right.Cast<(INamedTypeSymbol Type, Location Location)>().ToList(),
                source.Left.Right.Cast<PropertyInfo>().ToList(),
                source.Right,
                spc));
    }

    private static (INamedTypeSymbol Type, Location Location)? GetClassInfo(GeneratorSyntaxContext context)
    {
        if (context.Node is not ClassDeclarationSyntax classDeclaration)
            return null;

        foreach (var attributeList in classDeclaration.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var name = attribute.Name.ToString();
                if (name is "Reactive" or "ReactiveAttribute")
                {
                    var symbol = (INamedTypeSymbol?)context.SemanticModel.GetDeclaredSymbol(classDeclaration);
                    if (symbol != null)
                    {
                        return (symbol, classDeclaration.GetLocation());
                    }
                }
            }
        }

        return null;
    }

    private static PropertyInfo? GetPropertyInfo(GeneratorSyntaxContext context)
    {
        if (context.Node is not PropertyDeclarationSyntax propertyDeclaration)
            return null;

        var symbol = context.SemanticModel.GetDeclaredSymbol(propertyDeclaration) as IPropertySymbol;
        if (symbol == null)
            return null;

        bool hasReactiveAttribute = false;
        bool hasIgnoreAttribute = false;

        foreach (var attributeList in propertyDeclaration.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var name = attribute.Name.ToString();
                if (name is "Reactive" or "ReactiveAttribute")
                    hasReactiveAttribute = true;
                else if (name is "IgnoreReactive" or "IgnoreReactiveAttribute")
                    hasIgnoreAttribute = true;
            }
        }

        // Check if containing type has [Reactive] attribute
        var containingType = symbol.ContainingType;
        bool classHasReactiveAttribute = false;
        foreach (var attribute in containingType.GetAttributes())
        {
            if (attribute.AttributeClass?.Name is "ReactiveAttribute" or "Reactive")
            {
                classHasReactiveAttribute = true;
                break;
            }
        }

        // Check if property has an implementation
        bool hasImplementation = propertyDeclaration.AccessorList?.Accessors.Any(
            a => a.Body != null || a.ExpressionBody != null) ?? false;

        // Return property info if it either:
        // 1. Has [Reactive] attribute directly
        // 2. Is in a class with [Reactive] attribute and doesn't have [IgnoreReactive]
        // 3. Has no implementation yet
        if ((hasReactiveAttribute || (classHasReactiveAttribute && !hasIgnoreAttribute)) && !hasImplementation)
        {
            return new PropertyInfo(symbol, hasReactiveAttribute, hasIgnoreAttribute, hasImplementation);
        }

        return null;
    }

    private static bool InheritsFromReactiveObject(INamedTypeSymbol typeSymbol)
    {
        var current = typeSymbol;
        while (current is not null)
        {
            if (current.Name == "ReactiveObject" &&
                current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).StartsWith("global::ReactiveUI."))
                return true;
            current = current.BaseType;
        }

        return false;
    }

    private static int GetTypeHierarchyDepth(INamedTypeSymbol type)
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

    private static void Execute(
        Compilation compilation,
        List<(INamedTypeSymbol Type, Location Location)> reactiveClasses,
        List<PropertyInfo> properties,
        bool useLegacyMode,
        SourceProductionContext context)
    {
        if (properties.Count == 0 && reactiveClasses.Count == 0)
            return;

        var processedTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var reactiveTypesSet = new HashSet<INamedTypeSymbol>(
            reactiveClasses.Select(rc => rc.Type),
            SymbolEqualityComparer.Default);

        // Group properties by containing type
        var propertyGroups = properties
            .GroupBy(p => p.Property.ContainingType, SymbolEqualityComparer.Default)
            .ToDictionary(g => g.Key, g => g.ToList(), SymbolEqualityComparer.Default);

        // Get all types that need processing
        var allTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        // Add types from reactive classes
        foreach (var reactiveClass in reactiveClasses)
            allTypes.Add(reactiveClass.Type);

        // Add types from properties that should be reactive and don't have implementation
        foreach (var property in properties)
        {
            if ((property.HasReactiveAttribute ||
                 (reactiveTypesSet.Contains(property.Property.ContainingType) && !property.HasIgnoreAttribute)) &&
                !property.HasImplementation &&
                property.Property.ContainingType is INamedTypeSymbol type)
            {
                allTypes.Add(type);
            }
        }

        // First pass: Process base types that need INPC
        var typesToProcess = allTypes.ToList(); // Create a copy to avoid modification during enumeration
        foreach (var type in typesToProcess)
        {
            var current = type.BaseType;
            while (current is not null)
            {
                allTypes.Add(current); // Add base type to processing queue
                current = current.BaseType;
            }
        }

        // Process types in correct order (base types first)
        foreach (var type in allTypes.OrderBy(t => GetTypeHierarchyDepth(t)))
        {
            // Skip if type already processed or inherits from ReactiveObject
            if (processedTypes.Contains(type) || InheritsFromReactiveObject(type))
                continue;

            // Check if type needs INPC implementation
            var needsInpc = !HasINPCImplementation(compilation, type, processedTypes) &&
                            (reactiveTypesSet.Contains(type) || // Has [Reactive] class attribute
                             propertyGroups.ContainsKey(type)); // Has properties that should be reactive

            if (needsInpc)
            {
                var inpcSource = GenerateINPCImplementation(type);
                if (!string.IsNullOrEmpty(inpcSource))
                {
                    // Create a unique filename using the full type name (including namespace)
                    var fullTypeName = type.ToDisplayString(new SymbolDisplayFormat(
                        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                        genericsOptions: SymbolDisplayGenericsOptions.None,
                        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.None));

                    var fileName = $"{fullTypeName}.INPC.g.cs";
                    context.AddSource(fileName, SourceText.From(inpcSource, Encoding.UTF8));
                    processedTypes.Add(type);
                }
            }
        }

        // Process properties
        foreach (var group in propertyGroups)
        {
            var typeSymbol = group.Key as INamedTypeSymbol;
            if (typeSymbol == null) continue;

            // Filter properties that should be reactive
            var reactiveProperties = group.Value
                .Where(p => p.HasReactiveAttribute ||
                            (reactiveTypesSet.Contains(typeSymbol) && !p.HasIgnoreAttribute))
                .Select(p => p.Property)
                .ToList();

            if (!reactiveProperties.Any())
                continue;

            var source = GenerateClassSource(
                typeSymbol,
                reactiveProperties,
                implementInpc: false, // INPC already implemented in first pass if needed
                useLegacyMode);

            if (!string.IsNullOrEmpty(source))
            {
                var fullTypeName = typeSymbol.ToDisplayString(new SymbolDisplayFormat(
                    typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                    genericsOptions: SymbolDisplayGenericsOptions.None,
                    miscellaneousOptions: SymbolDisplayMiscellaneousOptions.None));

                var fileName = $"{fullTypeName}.ReactiveProperties.g.cs";
                context.AddSource(fileName, SourceText.From(source, Encoding.UTF8));
            }
        }
    }

    private static bool HasINPCImplementation(Compilation compilation, INamedTypeSymbol typeSymbol,
        HashSet<INamedTypeSymbol> processedTypes)
    {
        var inpcType = compilation.GetTypeByMetadataName("System.ComponentModel.INotifyPropertyChanged");
        if (inpcType is null)
            return false;

        // First check if current type implements INPC directly
        if (typeSymbol.AllInterfaces.Contains(inpcType, SymbolEqualityComparer.Default))
            return true;

        // Check if current type is in processedTypes (will have INPC implemented)
        if (processedTypes.Contains(typeSymbol))
            return true;

        // Check base types recursively
        var current = typeSymbol.BaseType;
        while (current is not null)
        {
            // Check if base type implements INPC directly
            if (current.AllInterfaces.Contains(inpcType, SymbolEqualityComparer.Default))
                return true;

            // Check if base type is in processedTypes
            if (processedTypes.Contains(current))
                return true;

            current = current.BaseType;
        }

        return false;
    }

    private static string GetAccessorAccessibility(IMethodSymbol? accessor)
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

    private static string FormatTypeNameForXmlDoc(ITypeSymbol type)
    {
        var format = new SymbolDisplayFormat(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

        return type.ToDisplayString(format).Replace("<", "{").Replace(">", "}");
    }

    private static string GenerateINPCImplementation(INamedTypeSymbol classSymbol)
    {
        // Helper method to format type names for XML docs
        string FormatTypeNameForXmlDoc(ITypeSymbol type)
        {
            var minimalFormat = new SymbolDisplayFormat(
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
                miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

            return type.ToDisplayString(minimalFormat).Replace("<", "{").Replace(">", "}");
        }

        // Helper method to get containing types chain
        IEnumerable<INamedTypeSymbol> GetContainingTypesChain(INamedTypeSymbol symbol)
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

        // Get namespace and containing types
        var namespaceName = classSymbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : classSymbol.ContainingNamespace.ToDisplayString();

        var containingTypes = GetContainingTypesChain(classSymbol).ToList();

        // Add type parameters if the class is generic
        var typeParameters = "";
        var typeConstraints = "";
        if (classSymbol.TypeParameters.Length > 0)
        {
            typeParameters = "<" + string.Join(", ", classSymbol.TypeParameters.Select(tp => tp.Name)) + ">";

            // Add constraints for each type parameter
            var constraints = new List<string>();
            foreach (var typeParam in classSymbol.TypeParameters)
            {
                var paramConstraints = new List<string>();

                if (typeParam.HasReferenceTypeConstraint)
                    paramConstraints.Add("class");
                if (typeParam.HasValueTypeConstraint)
                    paramConstraints.Add("struct");
                if (typeParam.HasConstructorConstraint)
                    paramConstraints.Add("new()");

                var typeConstraint = string.Join(", ", typeParam.ConstraintTypes.Select(t =>
                    t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                if (!string.IsNullOrEmpty(typeConstraint))
                    paramConstraints.Add(typeConstraint);

                if (paramConstraints.Count > 0)
                    constraints.Add($"where {typeParam.Name} : {string.Join(", ", paramConstraints)}");
            }

            if (constraints.Count > 0)
                typeConstraints = " " + string.Join(" ", constraints);
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System.ComponentModel;");
        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine();

        // Add namespace
        if (namespaceName != null)
        {
            sb.AppendLine($"namespace {namespaceName}");
            sb.AppendLine("{");
        }

        // Start containing type declarations
        var indent = namespaceName != null ? "    " : "";
        foreach (var containingType in containingTypes)
        {
            var containingTypeAccessibility = containingType.DeclaredAccessibility.ToString().ToLowerInvariant();
            sb.AppendLine($"{indent}{containingTypeAccessibility} partial class {containingType.Name}");
            sb.AppendLine($"{indent}{{");
            indent += "    ";
        }

        var accessibility = classSymbol.DeclaredAccessibility.ToString().ToLowerInvariant();

        // Add XML documentation comment if the class is public
        if (classSymbol.DeclaredAccessibility == Accessibility.Public)
        {
            var xmlClassName = FormatTypeNameForXmlDoc(classSymbol);
            sb.AppendLine($"{indent}/// <summary>");
            sb.AppendLine(
                $"{indent}/// A partial class implementation of <see cref=\"INotifyPropertyChanged\"/> for <see cref=\"{xmlClassName}\"/>.");
            sb.AppendLine($"{indent}/// </summary>");
        }

        sb.AppendLine(
            $"{indent}{accessibility} partial class {classSymbol.Name}{typeParameters} : INotifyPropertyChanged{typeConstraints}");
        sb.AppendLine($"{indent}{{");

        // Add XML documentation comment for the event if it's public
        if (classSymbol.DeclaredAccessibility == Accessibility.Public)
        {
            sb.AppendLine($"{indent}    /// <summary>");
            sb.AppendLine($"{indent}    /// Occurs when a property value changes.");
            sb.AppendLine($"{indent}    /// </summary>");
            sb.AppendLine($"{indent}    /// <seealso cref=\"INotifyPropertyChanged\"/>");
        }

        sb.AppendLine($"{indent}    public event PropertyChangedEventHandler? PropertyChanged;");
        sb.AppendLine();

        // Add XML documentation comment for OnPropertyChanged method
        sb.AppendLine($"{indent}    /// <summary>");
        sb.AppendLine($"{indent}    /// Raises the <see cref=\"PropertyChanged\"/> event.");
        sb.AppendLine($"{indent}    /// </summary>");
        sb.AppendLine($"{indent}    /// <param name=\"propertyName\">The name of the property that changed.</param>");
        sb.AppendLine(
            $"{indent}    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)");
        sb.AppendLine($"{indent}    {{");
        sb.AppendLine($"{indent}        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));");
        sb.AppendLine($"{indent}    }}");
        sb.AppendLine();

        sb.AppendLine($"{indent}    /// <summary>");
        sb.AppendLine($"{indent}    /// Raises the <see cref=\"PropertyChanged\"/> event.");
        sb.AppendLine($"{indent}    /// </summary>");
        sb.AppendLine(
            $"{indent}    /// <param name=\"args\">The <see cref=\"PropertyChangedEventArgs\"/> instance containing the event data.</param>");
        sb.AppendLine($"{indent}    protected virtual void OnPropertyChanged(PropertyChangedEventArgs args)");
        sb.AppendLine($"{indent}    {{");
        sb.AppendLine($"{indent}        PropertyChanged?.Invoke(this, args);");
        sb.AppendLine($"{indent}    }}");
        sb.AppendLine($"{indent}}}");

        // Close all containing type declarations
        for (int i = 0; i < containingTypes.Count; i++)
        {
            indent = indent.Substring(0, indent.Length - 4);
            sb.AppendLine($"{indent}}}");
        }

        // Close namespace
        if (namespaceName != null)
        {
            sb.AppendLine("}");
        }

        return sb.ToString();
    }

    private static string GenerateClassSource(
        INamedTypeSymbol classSymbol,
        List<IPropertySymbol> properties,
        bool implementInpc,
        bool useLegacyMode)
    {
        if (!properties.Any() && !implementInpc)
            return string.Empty;

        // Helper method to get containing types chain
        IEnumerable<INamedTypeSymbol> GetContainingTypesChain(INamedTypeSymbol symbol)
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

        var typeProperties = properties
            .Where(p => SymbolEqualityComparer.Default.Equals(p.ContainingType, classSymbol))
            .ToList();

        var isReactiveObject = InheritsFromReactiveObject(classSymbol);
        var namespaceName = classSymbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : classSymbol.ContainingNamespace.ToDisplayString();

        var containingTypes = GetContainingTypesChain(classSymbol).ToList();

        // Add type parameters if the class is generic
        var typeParameters = "";
        var typeConstraints = "";
        if (classSymbol.TypeParameters.Length > 0)
        {
            typeParameters = "<" + string.Join(", ", classSymbol.TypeParameters.Select(tp => tp.Name)) + ">";

            // Add constraints for each type parameter
            var constraints = new List<string>();
            foreach (var typeParam in classSymbol.TypeParameters)
            {
                var paramConstraints = new List<string>();

                if (typeParam.HasReferenceTypeConstraint)
                    paramConstraints.Add("class");
                if (typeParam.HasValueTypeConstraint)
                    paramConstraints.Add("struct");
                if (typeParam.HasConstructorConstraint)
                    paramConstraints.Add("new()");

                var typeConstraint = string.Join(", ", typeParam.ConstraintTypes.Select(t =>
                    t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                if (!string.IsNullOrEmpty(typeConstraint))
                    paramConstraints.Add(typeConstraint);

                if (paramConstraints.Count > 0)
                    constraints.Add($"where {typeParam.Name} : {string.Join(", ", paramConstraints)}");
            }

            if (constraints.Count > 0)
                typeConstraints = " " + string.Join(" ", constraints);
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        if (isReactiveObject)
        {
            sb.AppendLine("using ReactiveUI;");
        }
        else
        {
            sb.AppendLine("using System.ComponentModel;");
            sb.AppendLine("using System.Runtime.CompilerServices;");
        }

        sb.AppendLine();

        // Add namespace
        if (namespaceName != null)
        {
            sb.AppendLine($"namespace {namespaceName}");
            sb.AppendLine("{");
        }

        // Start containing type declarations
        var indent = namespaceName != null ? "    " : "";
        foreach (var containingType in containingTypes)
        {
            var containingTypeAccessibility = containingType.DeclaredAccessibility.ToString().ToLowerInvariant();
            sb.AppendLine($"{indent}{containingTypeAccessibility} partial class {containingType.Name}");
            sb.AppendLine($"{indent}{{");
            indent += "    ";
        }

        var accessibility = classSymbol.DeclaredAccessibility.ToString().ToLowerInvariant();
        var interfaces = (implementInpc && !isReactiveObject) ? " : INotifyPropertyChanged" : "";

        // Add XML documentation comment if the class is public
        if (classSymbol.DeclaredAccessibility == Accessibility.Public)
        {
            var xmlClassName = FormatTypeNameForXmlDoc(classSymbol);
            sb.AppendLine($"{indent}/// <summary>");
            sb.AppendLine($"{indent}/// A partial class implementation for {xmlClassName}.");
            sb.AppendLine($"{indent}/// </summary>");
        }

        sb.AppendLine(
            $"{indent}{accessibility} partial class {classSymbol.Name}{typeParameters}{interfaces}{typeConstraints}");
        sb.AppendLine($"{indent}{{");
        indent += "    ";

        // Generate backing fields if in legacy mode
        if (useLegacyMode && typeProperties.Any())
        {
            foreach (var property in typeProperties)
            {
                var propertyType = GetPropertyTypeWithNullability(property);
                var backingFieldName = GetBackingFieldName(property.Name);
                sb.AppendLine($"{indent}private {propertyType} {backingFieldName};");
            }

            sb.AppendLine();
        }

        if (!isReactiveObject && typeProperties.Any())
        {
            foreach (var property in typeProperties)
            {
                var propertyName = property.Name;
                var fieldName = GetEventArgsFieldName(propertyName);
                sb.AppendLine(
                    $"{indent}private static readonly PropertyChangedEventArgs {fieldName} = new PropertyChangedEventArgs(nameof({propertyName}));");
            }

            if (typeProperties.Any())
                sb.AppendLine();
        }

        if (implementInpc && !isReactiveObject)
        {
            // Add XML documentation comment for the event if it's public
            if (classSymbol.DeclaredAccessibility == Accessibility.Public)
            {
                sb.AppendLine($"{indent}/// <summary>");
                sb.AppendLine($"{indent}/// Occurs when a property value changes.");
                sb.AppendLine($"{indent}/// </summary>");
                sb.AppendLine($"{indent}/// <seealso cref=\"INotifyPropertyChanged\"/>");
            }

            sb.AppendLine($"{indent}public event PropertyChangedEventHandler? PropertyChanged;");
            sb.AppendLine();

            // Add XML documentation comment for OnPropertyChanged method
            sb.AppendLine($"{indent}/// <summary>");
            sb.AppendLine($"{indent}/// Raises the PropertyChanged event.");
            sb.AppendLine($"{indent}/// </summary>");
            sb.AppendLine($"{indent}/// <param name=\"propertyName\">The name of the property that changed.</param>");
            sb.AppendLine(
                $"{indent}protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)");
            sb.AppendLine($"{indent}{{");
            sb.AppendLine($"{indent}    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));");
            sb.AppendLine($"{indent}}}");
            sb.AppendLine();

            sb.AppendLine($"{indent}/// <summary>");
            sb.AppendLine($"{indent}/// Raises the PropertyChanged event.");
            sb.AppendLine($"{indent}/// </summary>");
            sb.AppendLine($"{indent}/// <param name=\"args\">The PropertyChangedEventArgs.</param>");
            sb.AppendLine($"{indent}protected virtual void OnPropertyChanged(PropertyChangedEventArgs args)");
            sb.AppendLine($"{indent}{{");
            sb.AppendLine($"{indent}    PropertyChanged?.Invoke(this, args);");
            sb.AppendLine($"{indent}}}");
            sb.AppendLine();
        }

        // Generate properties
        if (typeProperties.Any())
        {
            var lastProperty = typeProperties.Last();
            foreach (var property in typeProperties)
            {
                var propertyAccessibility = property.DeclaredAccessibility.ToString().ToLowerInvariant();

                if (useLegacyMode)
                {
                    var backingFieldName = GetBackingFieldName(property.Name);
                    GenerateLegacyProperty(sb, property, propertyAccessibility, backingFieldName, isReactiveObject,
                        indent);
                }
                else
                {
                    GenerateFieldKeywordProperty(sb, property, propertyAccessibility, isReactiveObject, indent);
                }

                if (!SymbolEqualityComparer.Default.Equals(property, lastProperty))
                {
                    sb.AppendLine();
                }
            }
        }

        // Close class declaration
        indent = indent.Substring(4);
        sb.AppendLine($"{indent}}}");

        // Close containing type declarations
        for (int i = 0; i < containingTypes.Count; i++)
        {
            indent = indent.Substring(4);
            sb.AppendLine($"{indent}}}");
        }

        // Close namespace
        if (namespaceName != null)
        {
            sb.AppendLine("}");
        }

        return sb.ToString();
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

    private static void GenerateLegacyProperty(
        StringBuilder sb,
        IPropertySymbol property,
        string propertyAccessibility,
        string backingFieldName,
        bool isReactiveObject,
        string indent)
    {
        var propertyName = property.Name;
        var propertyType = GetPropertyTypeWithNullability(property);
        var getterAccessibility = GetAccessorAccessibility(property.GetMethod);
        var setterAccessibility = GetAccessorAccessibility(property.SetMethod);

        if (property.DeclaredAccessibility == Accessibility.Public)
        {
            var xmlPropertyType = FormatTypeNameForXmlDoc(property.Type);
            sb.AppendLine($"{indent}/// <summary>");
            sb.AppendLine($"{indent}/// Gets or sets a value of type {xmlPropertyType}.");
            sb.AppendLine($"{indent}/// </summary>");
            sb.AppendLine($"{indent}/// <value>The value of type {xmlPropertyType}.</value>");
        }

        sb.AppendLine($"{indent}{propertyAccessibility} partial {propertyType} {propertyName}");
        sb.AppendLine($"{indent}{{");

        if (isReactiveObject)
        {
            var getterModifier = getterAccessibility != propertyAccessibility ? $"{getterAccessibility} " : "";
            var setterModifier = setterAccessibility != propertyAccessibility ? $"{setterAccessibility} " : "";

            sb.AppendLine($"{indent}    {getterModifier}get => {backingFieldName};");
            if (property.SetMethod != null)
            {
                sb.AppendLine(
                    $"{indent}    {setterModifier}set => this.RaiseAndSetIfChanged(ref {backingFieldName}, value);");
            }
        }
        else
        {
            var getterModifier = getterAccessibility != propertyAccessibility ? $"{getterAccessibility} " : "";
            var eventArgsFieldName = GetEventArgsFieldName(propertyName);

            sb.AppendLine($"{indent}    {getterModifier}get => {backingFieldName};");

            if (property.SetMethod != null)
            {
                var setterModifier = setterAccessibility != propertyAccessibility ? $"{setterAccessibility} " : "";
                sb.AppendLine($"{indent}    {setterModifier}set");
                sb.AppendLine($"{indent}    {{");
                sb.AppendLine($"{indent}        if (!Equals({backingFieldName}, value))");
                sb.AppendLine($"{indent}        {{");
                sb.AppendLine($"{indent}            {backingFieldName} = value;");
                sb.AppendLine($"{indent}            OnPropertyChanged({eventArgsFieldName});");
                sb.AppendLine($"{indent}        }}");
                sb.AppendLine($"{indent}    }}");
            }
        }

        sb.AppendLine($"{indent}}}");
    }

    private static void GenerateFieldKeywordProperty(
        StringBuilder sb,
        IPropertySymbol property,
        string propertyAccessibility,
        bool isReactiveObject,
        string indent)
    {
        var propertyName = property.Name;
        var propertyType = GetPropertyTypeWithNullability(property);
        var getterAccessibility = GetAccessorAccessibility(property.GetMethod);
        var setterAccessibility = GetAccessorAccessibility(property.SetMethod);

        if (property.DeclaredAccessibility == Accessibility.Public)
        {
            var xmlPropertyType = FormatTypeNameForXmlDoc(property.Type);
            sb.AppendLine($"{indent}/// <summary>");
            sb.AppendLine($"{indent}/// Gets or sets a value of type {xmlPropertyType}.");
            sb.AppendLine($"{indent}/// </summary>");
            sb.AppendLine($"{indent}/// <value>The value of type {xmlPropertyType}.</value>");
        }

        sb.AppendLine($"{indent}{propertyAccessibility} partial {propertyType} {propertyName}");
        sb.AppendLine($"{indent}{{");

        if (isReactiveObject)
        {
            var getterModifier = getterAccessibility != propertyAccessibility ? $"{getterAccessibility} " : "";
            var setterModifier = setterAccessibility != propertyAccessibility ? $"{setterAccessibility} " : "";

            sb.AppendLine($"{indent}    {getterModifier}get => field;");
            if (property.SetMethod != null)
            {
                sb.AppendLine($"{indent}    {setterModifier}set => this.RaiseAndSetIfChanged(ref field, value);");
            }
        }
        else
        {
            var getterModifier = getterAccessibility != propertyAccessibility ? $"{getterAccessibility} " : "";
            var eventArgsFieldName = GetEventArgsFieldName(propertyName);

            sb.AppendLine($"{indent}    {getterModifier}get => field;");

            if (property.SetMethod != null)
            {
                var setterModifier = setterAccessibility != propertyAccessibility ? $"{setterAccessibility} " : "";
                sb.AppendLine($"{indent}    {setterModifier}set");
                sb.AppendLine($"{indent}    {{");
                sb.AppendLine($"{indent}        if (!Equals(field, value))");
                sb.AppendLine($"{indent}        {{");
                sb.AppendLine($"{indent}            field = value;");
                sb.AppendLine($"{indent}            OnPropertyChanged({eventArgsFieldName});");
                sb.AppendLine($"{indent}        }}");
                sb.AppendLine($"{indent}    }}");
            }
        }

        sb.AppendLine($"{indent}}}");
    }

    private static string GetBackingFieldName(string propertyName)
    {
        return "_" + char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1);
    }

    private static string GetEventArgsFieldName(string propertyName)
    {
        return "_" + char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1) + "ChangedEventArgs";
    }

    private const string AttributeSource = @"using System;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
sealed class ReactiveAttribute : Attribute
{
    public ReactiveAttribute() { }
}";

    private const string IgnoreAttributeSource = @"using System;

[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
sealed class IgnoreReactiveAttribute : Attribute
{
    public IgnoreReactiveAttribute() { }
}";
}
