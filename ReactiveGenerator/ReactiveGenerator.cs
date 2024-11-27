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

        // Get both class and property declarations
        var declarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: (s, _) => IsCandidate(s),
                transform: (ctx, _) => GetSymbolInfo(ctx))
            .Where(m => m is not null);

        // Combine compilation, declarations, and configuration
        var compilationAndDeclarations = context.CompilationProvider
            .Combine(declarations.Collect())
            .Combine(useLegacyMode);

        context.RegisterSourceOutput(
            compilationAndDeclarations,
            (spc, source) => Execute(
                source.Left.Left,
                source.Left.Right.Cast<(ISymbol Symbol, Location Location, bool IsClass)>().ToList(),
                source.Right,
                spc));
    }

    private static bool IsCandidate(SyntaxNode node)
    {
        // Check for partial property with [Reactive] attribute
        if (node is PropertyDeclarationSyntax propertyDeclaration)
        {
            if (!propertyDeclaration.Modifiers.Any(m => m.ValueText == "partial"))
                return false;

            return propertyDeclaration.AttributeLists.Count > 0;
        }

        // Check for partial class with [Reactive] attribute
        if (node is ClassDeclarationSyntax classDeclaration)
        {
            if (!classDeclaration.Modifiers.Any(m => m.ValueText == "partial"))
                return false;

            return classDeclaration.AttributeLists.Count > 0;
        }

        return false;
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

    private static INamedTypeSymbol? FindFirstTypeNeedingINPC(Compilation compilation, INamedTypeSymbol typeSymbol)
    {
        var inpcType = compilation.GetTypeByMetadataName("System.ComponentModel.INotifyPropertyChanged");
        if (inpcType is null)
            return typeSymbol;

        if (InheritsFromReactiveObject(typeSymbol))
            return null;

        var current = typeSymbol;
        while (current is not null)
        {
            if (current.AllInterfaces.Contains(inpcType, SymbolEqualityComparer.Default))
            {
                return null;
            }

            if (current.BaseType is null ||
                !HasReactivePropertiesInHierarchy(current.BaseType))
            {
                return current;
            }

            current = current.BaseType;
        }

        return typeSymbol;
    }

   private static void Execute(
        Compilation compilation,
        List<(ISymbol Symbol, Location Location, bool IsClass)> declarations,
        bool useLegacyMode,
        SourceProductionContext context)
    {
        if (declarations.Count == 0)
            return;

        var processedTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var reactiveClasses = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        // First, identify and process class-level attributes to implement INPC
        foreach (var declaration in declarations.Where(d => d.IsClass))
        {
            var typeSymbol = (INamedTypeSymbol)declaration.Symbol;
            
            // Skip if type already implements INPC or inherits from ReactiveObject
            if (InheritsFromReactiveObject(typeSymbol) || 
                HasINPCImplementation(compilation, typeSymbol))
                continue;

            var source = GenerateINPCImplementation(typeSymbol);
            if (!string.IsNullOrEmpty(source))
            {
                var sourceFilePath = Path.GetFileNameWithoutExtension(declaration.Location.SourceTree?.FilePath ?? string.Empty);
                var fileName = $"{typeSymbol.Name}.{sourceFilePath}.INPC.g.cs";
                context.AddSource(fileName, SourceText.From(source, Encoding.UTF8));
                processedTypes.Add(typeSymbol);
                reactiveClasses.Add(typeSymbol);
            }
        }

        // Then handle property-level attributes
        var properties = declarations
            .Where(d => !d.IsClass)
            .Select(d => ((IPropertySymbol)d.Symbol, d.Location))
            .ToList();

        if (properties.Any())
        {
            // Group properties by containing type and file path
            var propertyGroups = properties
                .GroupBy(
                    p => (Type: p.Item1.ContainingType, 
                          FilePath: p.Location.SourceTree?.FilePath ?? string.Empty),
                    (key, group) => new
                    {
                        TypeSymbol = key.Type,
                        FilePath = key.FilePath,
                        Properties = group.Select(g => g.Item1).ToList()
                    },
                    new TypeAndPathComparer())
                .ToList();

            // Process INPC implementation for base types first if needed
            foreach (var group in propertyGroups)
            {
                if (group.TypeSymbol is not INamedTypeSymbol typeSymbol) continue;

                var baseType = FindFirstTypeNeedingINPC(compilation, typeSymbol);
                if (baseType is not null && !processedTypes.Contains(baseType) && 
                    !HasReactiveAttribute(baseType) && !reactiveClasses.Contains(baseType))
                {
                    var source = GenerateClassSource(baseType, properties.Select(p => p.Item1).ToList(), implementInpc: true, useLegacyMode);
                    if (!string.IsNullOrEmpty(source))
                    {
                        var sourceFilePath = Path.GetFileNameWithoutExtension(group.FilePath);
                        var fileName = $"{baseType.Name}.{sourceFilePath}.ReactiveProperties.g.cs";
                        context.AddSource(fileName, SourceText.From(source, Encoding.UTF8));
                        processedTypes.Add(baseType);
                    }
                }
            }

            // Process each group of properties
            foreach (var group in propertyGroups)
            {
                if (group.TypeSymbol is not INamedTypeSymbol typeSymbol || processedTypes.Contains(typeSymbol)) 
                    continue;

                var hasClassLevelReactive = HasReactiveAttribute(typeSymbol) || reactiveClasses.Contains(typeSymbol);
                var source = GenerateClassSource(typeSymbol, group.Properties, implementInpc: !hasClassLevelReactive, useLegacyMode);
                if (!string.IsNullOrEmpty(source))
                {
                    var sourceFilePath = Path.GetFileNameWithoutExtension(group.FilePath);
                    var fileName = $"{typeSymbol.Name}.{sourceFilePath}.ReactiveProperties.g.cs";
                    context.AddSource(fileName, SourceText.From(source, Encoding.UTF8));
                }
            }
        }
    }

    private static bool HasINPCImplementation(Compilation compilation, INamedTypeSymbol typeSymbol)
    {
        var inpcType = compilation.GetTypeByMetadataName("System.ComponentModel.INotifyPropertyChanged");
        if (inpcType is null)
            return false;

        var current = typeSymbol;
        while (current is not null)
        {
            if (current.AllInterfaces.Contains(inpcType, SymbolEqualityComparer.Default))
            {
                return true;
            }
            current = current.BaseType;
        }

        return false;
    }

    private static bool HasReactiveAttribute(INamedTypeSymbol typeSymbol)
    {
        return typeSymbol.GetAttributes().Any(attr =>
            attr.AttributeClass is not null &&
            (attr.AttributeClass.Name is "ReactiveAttribute" or "Reactive"));
    }
    
    private class TypeAndPathComparer : IEqualityComparer<(INamedTypeSymbol Type, string FilePath)>
    {
        public bool Equals((INamedTypeSymbol Type, string FilePath) x, (INamedTypeSymbol Type, string FilePath) y)
        {
            return SymbolEqualityComparer.Default.Equals(x.Type, y.Type) &&
                   string.Equals(x.FilePath, y.FilePath, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode((INamedTypeSymbol Type, string FilePath) obj)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + SymbolEqualityComparer.Default.GetHashCode(obj.Type);
                hash = hash * 31 + StringComparer.OrdinalIgnoreCase.GetHashCode(obj.FilePath);
                return hash;
            }
        }
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
