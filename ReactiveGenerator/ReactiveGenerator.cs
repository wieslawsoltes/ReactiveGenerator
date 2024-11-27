using System;
using System.Collections.Generic;
using System.IO;
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
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Register the attribute source with updated AttributeUsage
        context.RegisterPostInitializationOutput(ctx =>
        {
            ctx.AddSource("ReactiveAttribute.g.cs", SourceText.From(AttributeSource, Encoding.UTF8));
        });

        // Get MSBuild property for enabling legacy mode
        var useLegacyMode = context.AnalyzerConfigOptionsProvider
            .Select((provider, _) => bool.TryParse(
                provider.GlobalOptions.TryGetValue("build_property.UseBackingFields", out var value)
                    ? value
                    : "false",
                out var result) && result);

        // Get both partial class declarations and partial properties
        var partialClasses = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: (s, _) => s is ClassDeclarationSyntax c && 
                                    c.Modifiers.Any(m => m.ValueText == "partial") && 
                                    c.AttributeLists.Count > 0,
                transform: (ctx, _) => GetClassInfo(ctx))
            .Where(m => m is not null);

        var partialProperties = context.SyntaxProvider
            .CreateSyntaxProvider(
                // Update predicate to check for [Reactive] attribute
                predicate: (s, _) => s is PropertyDeclarationSyntax p && 
                                     p.Modifiers.Any(m => m.ValueText == "partial") &&
                                     p.AttributeLists.Any(al => al.Attributes.Any(a => 
                                         a.Name.ToString() is "Reactive" or "ReactiveAttribute")),
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
                source.Left.Right.Cast<(IPropertySymbol Property, Location Location)>().ToList(),
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
    
    private static (IPropertySymbol Property, Location Location)? GetPropertyInfo(GeneratorSyntaxContext context)
    {
        // Check if this is a property declaration
        if (context.Node is not PropertyDeclarationSyntax propertyDeclaration)
            return null;

        // Check if the property has the [Reactive] attribute
        bool hasReactiveAttribute = false;
        foreach (var attributeList in propertyDeclaration.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var name = attribute.Name.ToString();
                if (name is "Reactive" or "ReactiveAttribute")
                {
                    hasReactiveAttribute = true;
                    break;
                }
            }
            if (hasReactiveAttribute) break;
        }

        // If no [Reactive] attribute is found, skip this property
        if (!hasReactiveAttribute)
            return null;

        // Get the property symbol
        var symbol = context.SemanticModel.GetDeclaredSymbol(propertyDeclaration) as IPropertySymbol;
        if (symbol != null)
        {
            return (symbol, propertyDeclaration.GetLocation());
        }

        return null;
    }
    
    private static (ISymbol Symbol, Location Location, bool IsClass)? GetSymbolInfo(GeneratorSyntaxContext context)
    {
        var semanticModel = context.SemanticModel;

        switch (context.Node)
        {
            case PropertyDeclarationSyntax propertyDeclaration:
                foreach (var attributeList in propertyDeclaration.AttributeLists)
                {
                    foreach (var attribute in attributeList.Attributes)
                    {
                        var name = attribute.Name.ToString();
                        if (name is "Reactive" or "ReactiveAttribute")
                        {
                            var symbol = semanticModel.GetDeclaredSymbol(propertyDeclaration);
                            if (symbol != null)
                            {
                                return (symbol, propertyDeclaration.GetLocation(), false);
                            }
                        }
                    }
                }
                break;

            case ClassDeclarationSyntax classDeclaration:
                foreach (var attributeList in classDeclaration.AttributeLists)
                {
                    foreach (var attribute in attributeList.Attributes)
                    {
                        var name = attribute.Name.ToString();
                        if (name is "Reactive" or "ReactiveAttribute")
                        {
                            var symbol = semanticModel.GetDeclaredSymbol(classDeclaration);
                            if (symbol != null)
                            {
                                return (symbol, classDeclaration.GetLocation(), true);
                            }
                        }
                    }
                }
                break;
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

    private static bool HasReactivePropertiesInHierarchy(INamedTypeSymbol typeSymbol)
    {
        foreach (var member in typeSymbol.GetMembers().OfType<IPropertySymbol>())
        {
            if (member.GetAttributes().Any(attr =>
                    attr.AttributeClass is not null &&
                    (attr.AttributeClass.Name is "ReactiveAttribute" or "Reactive")))
            {
                return true;
            }
        }

        return typeSymbol.BaseType is not null && HasReactivePropertiesInHierarchy(typeSymbol.BaseType);
    }
    
    private static bool HasReactiveProperties(INamedTypeSymbol typeSymbol)
    {
        foreach (var member in typeSymbol.GetMembers().OfType<IPropertySymbol>())
        {
            if (member.GetAttributes().Any(attr =>
                    attr.AttributeClass is not null &&
                    (attr.AttributeClass.Name is "ReactiveAttribute" or "Reactive")))
            {
                return true;
            }
        }
        return false;
    }
    
    private static bool ShouldImplementINPC(
        Compilation compilation, 
        INamedTypeSymbol typeSymbol, 
        HashSet<INamedTypeSymbol> processedTypes)
    {
        // If the type inherits from ReactiveObject, it doesn't need INPC
        if (InheritsFromReactiveObject(typeSymbol))
            return false;

        // If it already implements INPC or will be implementing it, no need to add it again
        if (HasINPCImplementation(compilation, typeSymbol, processedTypes))
            return false;

        // Check if the type or any of its base types has the [Reactive] attribute
        var current = typeSymbol;
        while (current is not null)
        {
            if (current.GetAttributes().Any(attr =>
                    attr.AttributeClass is not null &&
                    (attr.AttributeClass.Name is "ReactiveAttribute" or "Reactive")))
            {
                return true;
            }
            current = current.BaseType;
        }

        // Even if no [Reactive] attribute on class, check if the type has any [Reactive] properties
        return HasReactiveProperties(typeSymbol);
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
    List<(IPropertySymbol Property, Location Location)> properties,
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
    
    // Add types from properties with [Reactive] attribute
    foreach (var property in properties)
    {
        if (property.Property.ContainingType is INamedTypeSymbol type)
            allTypes.Add(type);
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
                        propertyGroups.ContainsKey(type)); // Has [Reactive] properties

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

        var source = GenerateClassSource(
            typeSymbol,
            group.Value.Select(p => p.Property).ToList(),
            implementInpc: false, // INPC already implemented in first pass if needed
            useLegacyMode);

        if (!string.IsNullOrEmpty(source))
        {
            var fileName = $"{typeSymbol.Name}.ReactiveProperties.g.cs";
            context.AddSource(fileName, SourceText.From(source, Encoding.UTF8));
        }
    }
}

    private static bool HasINPCImplementation(Compilation compilation, INamedTypeSymbol typeSymbol, HashSet<INamedTypeSymbol> processedTypes)
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
        sb.AppendLine($"    {accessibility} partial class {classSymbol.Name} : INotifyPropertyChanged");
        sb.AppendLine("    {");

        sb.AppendLine("        public event PropertyChangedEventHandler? PropertyChanged;");
        sb.AppendLine();

        sb.AppendLine("        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)");
        sb.AppendLine("        {");
        sb.AppendLine("            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));");
        sb.AppendLine("        }");
        sb.AppendLine();

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
        sb.AppendLine($"    {accessibility} partial class {classSymbol.Name}{interfaces}");
        sb.AppendLine("    {");

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
            sb.AppendLine("        public event PropertyChangedEventHandler? PropertyChanged;");
            sb.AppendLine();

            sb.AppendLine(
                "        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)");
            sb.AppendLine("        {");
            sb.AppendLine("            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));");
            sb.AppendLine("        }");
            sb.AppendLine();

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
}
