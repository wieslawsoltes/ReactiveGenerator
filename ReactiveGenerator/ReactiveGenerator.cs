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
        bool HasImplementation)
    {
        public string GetPropertyModifiers()
        {
            var modifiers = new List<string>();

            if (Property.IsOverride)
                modifiers.Add("override");
            else if (Property.IsVirtual)
                modifiers.Add("virtual");
            else if (Property.IsAbstract)
                modifiers.Add("abstract");

            return string.Join(" ", modifiers);
        }
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Register attributes
        context.RegisterPostInitializationOutput(ctx =>
        {
            ctx.AddSource("ReactiveAttribute.g.cs", SourceText.From(AttributeSource, Encoding.UTF8));
            ctx.AddSource("IgnoreReactiveAttribute.g.cs", SourceText.From(IgnoreAttributeSource, Encoding.UTF8));
        });

        // Enhanced configuration handling
        var useLegacyMode = context.AnalyzerConfigOptionsProvider
            .Select((provider, _) =>
            {
                // Check new format first
                if (provider.GlobalOptions.TryGetValue("build_property.UseBackingFields", out var value))
                {
                    if (bool.TryParse(value, out var result))
                        return result;
                }

                // Then check legacy format
                if (provider.GlobalOptions.TryGetValue("UseBackingFields", out value))
                {
                    if (bool.TryParse(value, out var result))
                        return result;
                }

                return false;
            });

        // Get partial class declarations
        var partialClasses = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: (s, _) => s is ClassDeclarationSyntax c &&
                                     c.Modifiers.Any(m => m.ValueText == "partial") &&
                                     c.AttributeLists.Count > 0,
                transform: (ctx, _) => GetClassInfo(ctx))
            .Where(m => m is not null);

        // Get partial properties
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

        var symbol = (INamedTypeSymbol?)context.SemanticModel.GetDeclaredSymbol(classDeclaration);
        if (symbol == null)
            return null;

        // Check if this type should be reactive
        if (ReactiveDetectionHelper.IsTypeReactive(symbol))
        {
            return (symbol, classDeclaration.GetLocation());
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

        // Check if containing type should be reactive
        bool classHasReactiveAttribute = ReactiveDetectionHelper.IsTypeReactive(symbol.ContainingType);

        // Check if property has an implementation
        bool hasImplementation = propertyDeclaration.AccessorList?.Accessors.Any(
            a => a.Body != null || a.ExpressionBody != null) ?? false;

        if (ReactiveDetectionHelper.IsPropertyReactive(symbol, classHasReactiveAttribute) && !hasImplementation)
        {
            bool hasReactiveAttribute = symbol.GetAttributes()
                .Any(a => a.AttributeClass?.Name is "ReactiveAttribute" or "Reactive");
            bool hasIgnoreAttribute = symbol.GetAttributes()
                .Any(a => a.AttributeClass?.Name is "IgnoreReactiveAttribute" or "IgnoreReactive");

            return new PropertyInfo(symbol, hasReactiveAttribute, hasIgnoreAttribute, hasImplementation);
        }

        return null;
    }

    private static bool InheritsFromReactiveObject(INamedTypeSymbol typeSymbol)
    {
        return ReactiveDetectionHelper.InheritsFromReactiveObject(typeSymbol);
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

    private static IEnumerable<INamedTypeSymbol> GetAllTypesInCompilation(Compilation compilation)
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

    private static bool IsTypeMarkedReactive(INamedTypeSymbol type)
    {
        // Check if type has [Reactive]
        foreach (var attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass?.Name is "ReactiveAttribute" or "Reactive")
                return true;
        }

        // Check base types for [Reactive]
        var current = type.BaseType;
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

    private static bool HasAnyReactiveProperties(INamedTypeSymbol type,
        Dictionary<ISymbol, List<PropertyInfo>> propertyGroups)
    {
        if (propertyGroups.TryGetValue(type, out var properties))
        {
            return properties.Any(p => p.HasReactiveAttribute);
        }

        return false;
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
        var allTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        // Group properties by containing type
        var propertyGroups = properties
            .GroupBy(p => p.Property.ContainingType, SymbolEqualityComparer.Default)
            .ToDictionary(g => g.Key, g => g.ToList(), SymbolEqualityComparer.Default);

        // Add types that need processing
        foreach (var type in GetAllTypesInCompilation(compilation))
        {
            if (IsTypeMarkedReactive(type) || HasAnyReactiveProperties(type, propertyGroups))
            {
                allTypes.Add(type);
            }
        }

        // Process types in correct order
        foreach (var type in allTypes.OrderBy(t => GetTypeHierarchyDepth(t)))
        {
            if (processedTypes.Contains(type))
                continue;

            var isReactiveObjectDerived = InheritsFromReactiveObject(type);

            // Generate INPC implementation if needed
            if (!isReactiveObjectDerived && !HasINPCImplementation(compilation, type, processedTypes))
            {
                var inpcSource = GenerateINPCImplementation(type);
                if (!string.IsNullOrEmpty(inpcSource))
                {
                    var fullTypeName = type.ToDisplayString(new SymbolDisplayFormat(
                        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                        genericsOptions: SymbolDisplayGenericsOptions.None,
                        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.None));

                    var fileName = $"{fullTypeName}.INPC.g.cs";
                    context.AddSource(fileName, SourceText.From(inpcSource, Encoding.UTF8));
                }
            }

            // Generate property implementations
            if (propertyGroups.TryGetValue(type, out var typeProperties))
            {
                var isTypeReactive = IsTypeMarkedReactive(type);
                var reactiveProperties = typeProperties
                    .Where(p => !p.HasImplementation &&
                                !p.HasIgnoreAttribute &&
                                (p.HasReactiveAttribute || // Include properties marked with [Reactive]
                                 isTypeReactive)) // Include all properties if class is marked with [Reactive]
                    .Select(p => p.Property)
                    .ToList();

                if (reactiveProperties.Any())
                {
                    var source = GenerateClassSource(
                        type,
                        reactiveProperties,
                        implementInpc: false,
                        useLegacyMode);

                    if (!string.IsNullOrEmpty(source))
                    {
                        var fullTypeName = type.ToDisplayString(new SymbolDisplayFormat(
                            typeQualificationStyle: SymbolDisplayTypeQualificationStyle
                                .NameAndContainingTypesAndNamespaces,
                            genericsOptions: SymbolDisplayGenericsOptions.None,
                            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.None));

                        var fileName = $"{fullTypeName}.ReactiveProperties.g.cs";
                        context.AddSource(fileName, SourceText.From(source, Encoding.UTF8));
                    }
                }
            }

            processedTypes.Add(type);
        }
    }

    private static bool HasINPCImplementation(Compilation compilation, INamedTypeSymbol typeSymbol,
        HashSet<INamedTypeSymbol> processedTypes)
    {
        var inpcType = compilation.GetTypeByMetadataName("System.ComponentModel.INotifyPropertyChanged");
        if (inpcType is null)
            return false;

        // First check if current type implements INPC directly or is in processedTypes
        if (typeSymbol.AllInterfaces.Contains(inpcType, SymbolEqualityComparer.Default) ||
            processedTypes.Contains(typeSymbol))
            return true;

        // Check base types recursively
        var current = typeSymbol.BaseType;
        while (current is not null)
        {
            // If base type is in same assembly
            if (current.ContainingAssembly == typeSymbol.ContainingAssembly)
            {
                // Check for INPC implementation or presence in processedTypes
                if (current.AllInterfaces.Contains(inpcType, SymbolEqualityComparer.Default) ||
                    processedTypes.Contains(current))
                    return true;
            }
            // If base type is in different assembly
            else
            {
                // For external types, only check for actual INPC implementation
                if (current.AllInterfaces.Contains(inpcType, SymbolEqualityComparer.Default))
                    return true;
            }

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

        var namespaceName = classSymbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : classSymbol.ContainingNamespace.ToDisplayString();

        var containingTypes = GetContainingTypesChain(classSymbol).ToList();

        var typeParameters = "";
        var typeConstraints = "";
        if (classSymbol.TypeParameters.Length > 0)
        {
            typeParameters = "<" + string.Join(", ", classSymbol.TypeParameters.Select(tp => tp.Name)) + ">";

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
        sb.AppendLine(
            $"{indent}{accessibility} partial class {classSymbol.Name}{typeParameters} : INotifyPropertyChanged{typeConstraints}");
        sb.AppendLine($"{indent}{{");

        sb.AppendLine($"{indent}    public event PropertyChangedEventHandler? PropertyChanged;");
        sb.AppendLine();

        sb.AppendLine(
            $"{indent}    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)");
        sb.AppendLine($"{indent}    {{");
        sb.AppendLine($"{indent}        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));");
        sb.AppendLine($"{indent}    }}");
        sb.AppendLine();

        sb.AppendLine($"{indent}    protected virtual void OnPropertyChanged(PropertyChangedEventArgs args)");
        sb.AppendLine($"{indent}    {{");
        sb.AppendLine($"{indent}        PropertyChanged?.Invoke(this, args);");
        sb.AppendLine($"{indent}    }}");
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

        var propInfo = new PropertyInfo(property, false, false, false);
        var modifiers = propInfo.GetPropertyModifiers();

        var declarationModifiers = new List<string> { propertyAccessibility };
        if (!string.IsNullOrEmpty(modifiers))
            declarationModifiers.Add(modifiers);
        declarationModifiers.Add("partial");

        sb.AppendLine($"{indent}{string.Join(" ", declarationModifiers)} {propertyType} {propertyName}");
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

        // Create PropertyInfo to get modifiers
        var propInfo = new PropertyInfo(property, false, false, false);
        var modifiers = propInfo.GetPropertyModifiers();

        // Combine modifiers with accessibility and partial
        var declarationModifiers = new List<string> { propertyAccessibility };
        if (!string.IsNullOrEmpty(modifiers))
            declarationModifiers.Add(modifiers);
        declarationModifiers.Add("partial");

        sb.AppendLine($"{indent}{string.Join(" ", declarationModifiers)} {propertyType} {propertyName}");
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

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
sealed class ReactiveAttribute : Attribute
{
    public ReactiveAttribute() { }
}";

    private const string IgnoreAttributeSource = @"using System;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
sealed class IgnoreReactiveAttribute : Attribute
{
    public IgnoreReactiveAttribute() { }
}";
}
