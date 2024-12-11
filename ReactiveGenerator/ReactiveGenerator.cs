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
                    var fileName = $"{type.Name}.INPC.g.cs";
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
                var fileName = $"{typeSymbol.Name}.ReactiveProperties.g.cs";
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

        var namespaceName = classSymbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : classSymbol.ContainingNamespace.ToDisplayString();

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

        var accessibility = classSymbol.DeclaredAccessibility.ToString().ToLowerInvariant();

        // Add XML documentation comment if the class is public
        if (classSymbol.DeclaredAccessibility == Accessibility.Public)
        {
            var xmlClassName = FormatTypeNameForXmlDoc(classSymbol);
            sb.AppendLine("    /// <summary>");
            sb.AppendLine(
                $"    /// A partial class implementation of <see cref=\"INotifyPropertyChanged\"/> for <see cref=\"{xmlClassName}\"/>.");
            sb.AppendLine("    /// </summary>");
        }

        sb.AppendLine($"    {accessibility} partial class {classSymbol.Name} : INotifyPropertyChanged");
        sb.AppendLine("    {");

        // Add XML documentation comment for the event if it's public
        if (classSymbol.DeclaredAccessibility == Accessibility.Public)
        {
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Occurs when a property value changes.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        /// <seealso cref=\"INotifyPropertyChanged\"/>");
        }

        sb.AppendLine("        public event PropertyChangedEventHandler? PropertyChanged;");
        sb.AppendLine();

        // Add XML documentation comment for OnPropertyChanged method
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Raises the <see cref=\"PropertyChanged\"/> event.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        /// <param name=\"propertyName\">The name of the property that changed.</param>");
        sb.AppendLine(
            "        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)");
        sb.AppendLine("        {");
        sb.AppendLine("            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));");
        sb.AppendLine("        }");
        sb.AppendLine();

        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Raises the <see cref=\"PropertyChanged\"/> event.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine(
            "        /// <param name=\"args\">The <see cref=\"PropertyChangedEventArgs\"/> instance containing the event data.</param>");
        sb.AppendLine("        protected virtual void OnPropertyChanged(PropertyChangedEventArgs args)");
        sb.AppendLine("        {");
        sb.AppendLine("            PropertyChanged?.Invoke(this, args);");
        sb.AppendLine("        }");

        sb.AppendLine("    }");

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

        var typeProperties = properties
            .Where(p => SymbolEqualityComparer.Default.Equals(p.ContainingType, classSymbol))
            .ToList();

        var isReactiveObject = InheritsFromReactiveObject(classSymbol);
        var namespaceName = classSymbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : classSymbol.ContainingNamespace.ToDisplayString();

        // Helper method to format type names for XML docs
        string FormatTypeNameForXmlDoc(ITypeSymbol type)
        {
            var minimalFormat = new SymbolDisplayFormat(
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
                miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

            return type.ToDisplayString(minimalFormat).Replace("<", "{").Replace(">", "}");
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

        if (namespaceName != null)
        {
            sb.AppendLine($"namespace {namespaceName}");
            sb.AppendLine("{");
        }

        var accessibility = classSymbol.DeclaredAccessibility.ToString().ToLowerInvariant();
        var interfaces = (implementInpc && !isReactiveObject) ? " : INotifyPropertyChanged" : "";

        // Add type parameters if the class is generic
        var typeParameters = "";
        if (classSymbol.TypeParameters.Length > 0)
        {
            typeParameters = "<" + string.Join(", ", classSymbol.TypeParameters.Select(tp => tp.Name)) + ">";
        }

        // Add XML documentation comment if the class is public
        if (classSymbol.DeclaredAccessibility == Accessibility.Public)
        {
            var xmlClassName = FormatTypeNameForXmlDoc(classSymbol);
            sb.AppendLine("    /// <summary>");
            sb.AppendLine($"    /// A partial class implementation for <see cref=\"{xmlClassName}\"/>.");
            sb.AppendLine("    /// </summary>");
        }

        sb.AppendLine($"    {accessibility} partial class {classSymbol.Name}{typeParameters}{interfaces}");
        sb.AppendLine("    {");

        // Generate backing fields if in legacy mode
        if (useLegacyMode && typeProperties.Any())
        {
            foreach (var property in typeProperties)
            {
                var propertyType = GetPropertyTypeWithNullability(property);
                var backingFieldName = GetBackingFieldName(property.Name);
                sb.AppendLine($"        private {propertyType} {backingFieldName};");
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
                    $"        private static readonly PropertyChangedEventArgs {fieldName} = new PropertyChangedEventArgs(nameof({propertyName}));");
            }

            if (typeProperties.Any())
                sb.AppendLine();
        }

        if (implementInpc && !isReactiveObject)
        {
            // Add XML documentation comment for the event if it's public
            if (classSymbol.DeclaredAccessibility == Accessibility.Public)
            {
                sb.AppendLine("        /// <summary>");
                sb.AppendLine("        /// Occurs when a property value changes.");
                sb.AppendLine("        /// </summary>");
            }

            sb.AppendLine("        public event PropertyChangedEventHandler? PropertyChanged;");
            sb.AppendLine();

            // Add XML documentation comment for OnPropertyChanged method
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Raises the PropertyChanged event.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        /// <param name=\"propertyName\">The name of the property that changed.</param>");
            sb.AppendLine(
                "        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)");
            sb.AppendLine("        {");
            sb.AppendLine("            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// Raises the PropertyChanged event.");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        /// <param name=\"args\">The PropertyChangedEventArgs.</param>");
            sb.AppendLine("        protected virtual void OnPropertyChanged(PropertyChangedEventArgs args)");
            sb.AppendLine("        {");
            sb.AppendLine("            PropertyChanged?.Invoke(this, args);");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        // Generate properties
        if (typeProperties.Any())
        {
            var lastProperty = typeProperties.Last();
            foreach (var property in typeProperties)
            {
                var propertyAccessibility = property.DeclaredAccessibility.ToString().ToLowerInvariant();

                // Add XML documentation comment if the property is public
                if (property.DeclaredAccessibility == Accessibility.Public)
                {
                    var propertyTypeName = FormatTypeNameForXmlDoc(property.Type);
                    sb.AppendLine("        /// <summary>");
                    sb.AppendLine($"        /// Gets or sets a value for <see cref=\"{propertyTypeName}\"/>.");
                    sb.AppendLine("        /// </summary>");
                    sb.AppendLine($"        /// <value>The <see cref=\"{propertyTypeName}\"/> value.</value>");
                }

                if (useLegacyMode)
                {
                    var backingFieldName = GetBackingFieldName(property.Name);
                    GenerateLegacyProperty(sb, property, propertyAccessibility, backingFieldName, isReactiveObject);
                }
                else
                {
                    GenerateFieldKeywordProperty(sb, property, propertyAccessibility, isReactiveObject);
                }

                if (!SymbolEqualityComparer.Default.Equals(property, lastProperty))
                {
                    sb.AppendLine();
                }
            }
        }

        sb.AppendLine("    }");

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

        // If the type is a reference type and it's nullable, add the ? annotation
        if (nullableAnnotation == NullableAnnotation.Annotated &&
            !property.Type.IsValueType)
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
        bool isReactiveObject)
    {
        var propertyName = property.Name;
        var propertyType = GetPropertyTypeWithNullability(property);
        var getterAccessibility = GetAccessorAccessibility(property.GetMethod);
        var setterAccessibility = GetAccessorAccessibility(property.SetMethod);

        sb.AppendLine($"        {propertyAccessibility} partial {propertyType} {propertyName}");
        sb.AppendLine("        {");

        if (isReactiveObject)
        {
            var getterModifier = getterAccessibility != propertyAccessibility ? $"{getterAccessibility} " : "";
            var setterModifier = setterAccessibility != propertyAccessibility ? $"{setterAccessibility} " : "";

            sb.AppendLine($"            {getterModifier}get => {backingFieldName};");
            if (property.SetMethod != null)
            {
                sb.AppendLine(
                    $"            {setterModifier}set => this.RaiseAndSetIfChanged(ref {backingFieldName}, value);");
            }
        }
        else
        {
            var getterModifier = getterAccessibility != propertyAccessibility ? $"{getterAccessibility} " : "";
            var eventArgsFieldName = GetEventArgsFieldName(propertyName);

            sb.AppendLine($"            {getterModifier}get => {backingFieldName};");

            if (property.SetMethod != null)
            {
                var setterModifier = setterAccessibility != propertyAccessibility ? $"{setterAccessibility} " : "";
                sb.AppendLine($"            {setterModifier}set");
                sb.AppendLine("            {");
                sb.AppendLine($"                if (!Equals({backingFieldName}, value))");
                sb.AppendLine("                {");
                sb.AppendLine($"                    {backingFieldName} = value;");
                sb.AppendLine($"                    OnPropertyChanged({eventArgsFieldName});");
                sb.AppendLine("                }");
                sb.AppendLine("            }");
            }
        }

        sb.AppendLine("        }");
    }

    private static void GenerateFieldKeywordProperty(
        StringBuilder sb,
        IPropertySymbol property,
        string propertyAccessibility,
        bool isReactiveObject)
    {
        var propertyName = property.Name;
        var propertyType = GetPropertyTypeWithNullability(property);
        var getterAccessibility = GetAccessorAccessibility(property.GetMethod);
        var setterAccessibility = GetAccessorAccessibility(property.SetMethod);

        sb.AppendLine($"        {propertyAccessibility} partial {propertyType} {propertyName}");
        sb.AppendLine("        {");

        if (isReactiveObject)
        {
            var getterModifier = getterAccessibility != propertyAccessibility ? $"{getterAccessibility} " : "";
            var setterModifier = setterAccessibility != propertyAccessibility ? $"{setterAccessibility} " : "";

            sb.AppendLine($"            {getterModifier}get => field;");
            if (property.SetMethod != null)
            {
                sb.AppendLine($"            {setterModifier}set => this.RaiseAndSetIfChanged(ref field, value);");
            }
        }
        else
        {
            var getterModifier = getterAccessibility != propertyAccessibility ? $"{getterAccessibility} " : "";
            var eventArgsFieldName = GetEventArgsFieldName(propertyName);

            sb.AppendLine($"            {getterModifier}get => field;");

            if (property.SetMethod != null)
            {
                var setterModifier = setterAccessibility != propertyAccessibility ? $"{setterAccessibility} " : "";
                sb.AppendLine($"            {setterModifier}set");
                sb.AppendLine("            {");
                sb.AppendLine("                if (!Equals(field, value))");
                sb.AppendLine("                {");
                sb.AppendLine("                    field = value;");
                sb.AppendLine($"                    OnPropertyChanged({eventArgsFieldName});");
                sb.AppendLine("                }");
                sb.AppendLine("            }");
            }
        }

        sb.AppendLine("        }");
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
