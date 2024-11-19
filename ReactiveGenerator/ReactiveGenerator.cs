using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ReactiveGenerator
{
    [Generator]
    public class ReactiveGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Register our attribute source
            context.RegisterPostInitializationOutput(ctx =>
            {
                ctx.AddSource("ReactiveAttribute.g.cs", SourceText.From(AttributeSource, Encoding.UTF8));
            });

            // Get properties with [Reactive] attribute
            var propertyDeclarations = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: (s, _) => IsCandidateProperty(s),
                    transform: (ctx, _) => GetSemanticTarget(ctx))
                .Where(m => m != null);

            // Group properties by containing type and include the compilation
            var compilationAndProperties = context.CompilationProvider
                .Combine(propertyDeclarations.Collect());

            // Process each type that contains reactive properties
            context.RegisterSourceOutput(
                compilationAndProperties,
                (spc, source) => Execute(source.Left, source.Right.Cast<IPropertySymbol>().ToList(), spc));
        }

        private static bool IsCandidateProperty(SyntaxNode node)
        {
            if (node is not PropertyDeclarationSyntax propertyDeclaration)
                return false;

            // Must be partial property
            if (!propertyDeclaration.Modifiers.Any(m => m.ValueText == "partial"))
                return false;

            // Check for [Reactive] attribute
            return propertyDeclaration.AttributeLists.Count > 0;
        }

        private static IPropertySymbol GetSemanticTarget(GeneratorSyntaxContext context)
        {
            var propertyDeclaration = (PropertyDeclarationSyntax)context.Node;

            foreach (var attributeList in propertyDeclaration.AttributeLists)
            {
                foreach (var attribute in attributeList.Attributes)
                {
                    var name = attribute.Name.ToString();
                    if (name == "Reactive" || name == "ReactiveAttribute")
                    {
                        return context.SemanticModel.GetDeclaredSymbol(propertyDeclaration) as IPropertySymbol;
                    }
                }
            }

            return null;
        }

        private static bool InheritsFromReactiveObject(INamedTypeSymbol typeSymbol)
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

        private static void Execute(
            Compilation compilation,
            List<IPropertySymbol> properties,
            SourceProductionContext context)
        {
            if (properties.Count == 0)
                return;

            // Group properties by containing type
            var propertyGroups = properties
                .GroupBy(p => p.ContainingType, SymbolEqualityComparer.Default)
                .ToList();

            // Process all types that have [Reactive] properties
            var processedTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

            // First, find and process base types that need INPC
            foreach (var group in propertyGroups)
            {
                var typeSymbol = group.Key as INamedTypeSymbol;
                if (typeSymbol == null) continue;

                var baseType = FindFirstTypeNeedingINPC(compilation, typeSymbol);
                if (baseType != null && !processedTypes.Contains(baseType))
                {
                    var source = GenerateClassSource(compilation, baseType, properties, implementInpc: true);
                    if (!string.IsNullOrEmpty(source))
                    {
                        context.AddSource(
                            $"{baseType.Name}_ReactiveProperties.g.cs",
                            SourceText.From(source, Encoding.UTF8));
                        processedTypes.Add(baseType);
                    }
                }
            }

            // Then process all derived types that have [Reactive] properties
            foreach (var group in propertyGroups)
            {
                var typeSymbol = group.Key as INamedTypeSymbol;
                if (typeSymbol == null || processedTypes.Contains(typeSymbol)) continue;

                var source = GenerateClassSource(compilation, typeSymbol, properties, implementInpc: false);
                if (!string.IsNullOrEmpty(source))
                {
                    context.AddSource(
                        $"{typeSymbol.Name}_ReactiveProperties.g.cs",
                        SourceText.From(source, Encoding.UTF8));
                }
            }
        }

        private static INamedTypeSymbol FindFirstTypeNeedingINPC(Compilation compilation, INamedTypeSymbol typeSymbol)
        {
            var inpcType = compilation.GetTypeByMetadataName("System.ComponentModel.INotifyPropertyChanged");
            if (inpcType == null)
                return typeSymbol;

            // Check if the type inherits from ReactiveObject
            if (InheritsFromReactiveObject(typeSymbol))
                return null;  // No need for INPC implementation if it's a ReactiveObject

            var current = typeSymbol;
            while (current != null)
            {
                if (current.AllInterfaces.Contains(inpcType, SymbolEqualityComparer.Default))
                {
                    return null;  // Found existing INPC implementation
                }

                if (current.BaseType == null || 
                    !HasReactivePropertiesInHierarchy(current.BaseType))
                {
                    return current;  // This is the highest type that needs INPC
                }

                current = current.BaseType;
            }

            return typeSymbol;
        }

        private static string GenerateClassSource(
            Compilation compilation,
            INamedTypeSymbol classSymbol,
            List<IPropertySymbol> allProperties,
            bool implementInpc)
        {
            // Get properties for this specific type
            var typeProperties = allProperties.Where(p => 
                SymbolEqualityComparer.Default.Equals(p.ContainingType, classSymbol)).ToList();

            if (!typeProperties.Any())
                return string.Empty;

            var isReactiveObject = InheritsFromReactiveObject(classSymbol);
            var namespaceName = classSymbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : classSymbol.ContainingNamespace.ToDisplayString();

            var sb = new StringBuilder();
            
            // Add using statements
            if (isReactiveObject)
            {
                sb.AppendLine("using System.ComponentModel;");
                sb.AppendLine("using ReactiveUI;");
            }
            else
            {
                sb.AppendLine("using System.ComponentModel;");
                sb.AppendLine("using System.Runtime.CompilerServices;");
            }
            sb.AppendLine();

            // Begin namespace
            if (namespaceName != null)
            {
                sb.AppendLine($"namespace {namespaceName}");
                sb.AppendLine("{");
            }

            // Begin class
            var accessibility = classSymbol.DeclaredAccessibility.ToString().ToLowerInvariant();
            
            // Add INPC interface only if this is the base implementation and not a ReactiveObject
            var interfaces = (implementInpc && !isReactiveObject) ? " : INotifyPropertyChanged" : "";
            sb.AppendLine($"    {accessibility} partial class {classSymbol.Name}{interfaces}");
            sb.AppendLine("    {");

            // Add PropertyChanged event and methods only if implementing INPC and not ReactiveObject
            if (implementInpc && !isReactiveObject)
            {
                sb.AppendLine("        public event PropertyChangedEventHandler PropertyChanged;");
                sb.AppendLine();

                sb.AppendLine("        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)");
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

            // Add cached PropertyChangedEventArgs fields only if not using ReactiveObject
            if (!isReactiveObject)
            {
                foreach (var property in typeProperties)
                {
                    var propertyName = property.Name;
                    var fieldName = GetEventArgsFieldName(propertyName);
                    
                    sb.AppendLine($"        private static readonly PropertyChangedEventArgs {fieldName} = new PropertyChangedEventArgs(nameof({propertyName}));");
                }

                if (typeProperties.Any())
                    sb.AppendLine();
            }

            // Generate backing fields and properties
            foreach (var property in typeProperties)
            {
                var propertyName = property.Name;
                var propertyType = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var backingFieldName = GetBackingFieldName(propertyName);
                var eventArgsFieldName = GetEventArgsFieldName(propertyName);

                // Backing field should be private regardless of property accessibility
                sb.AppendLine($"        private {propertyType} {backingFieldName};");
                sb.AppendLine();

                // Get property accessors' modifiers
                var getterAccessibility = GetAccessorAccessibility(property.GetMethod);
                var setterAccessibility = GetAccessorAccessibility(property.SetMethod);
                var propertyAccessibility = property.DeclaredAccessibility.ToString().ToLowerInvariant();

                sb.AppendLine($"        {propertyAccessibility} partial {propertyType} {propertyName}");
                sb.AppendLine("        {");

                // Generate getter
                var getterModifier = getterAccessibility != propertyAccessibility ? $"{getterAccessibility} " : "";
                sb.AppendLine($"            {getterModifier}get => {backingFieldName};");

                // Generate setter if it exists
                if (property.SetMethod != null)
                {
                    var setterModifier = setterAccessibility != propertyAccessibility ? $"{setterAccessibility} " : "";
                    sb.AppendLine($"            {setterModifier}set");
                    sb.AppendLine("            {");
                    sb.AppendLine($"                if (!Equals({backingFieldName}, value))");
                    sb.AppendLine("                {");
                    sb.AppendLine($"                    {backingFieldName} = value;");
                    
                    // Use different notification method based on type
                    if (isReactiveObject)
                    {
                        sb.AppendLine($"                    this.RaiseAndSetIfChanged(ref {backingFieldName}, value);");
                    }
                    else
                    {
                        sb.AppendLine($"                    OnPropertyChanged({eventArgsFieldName});");
                    }
                    
                    sb.AppendLine("                }");
                    sb.AppendLine("            }");
                }

                sb.AppendLine("        }");
                sb.AppendLine();
            }

            sb.AppendLine("    }");

            if (namespaceName != null)
            {
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        private static bool HasReactivePropertiesInHierarchy(INamedTypeSymbol typeSymbol)
        {
            foreach (var member in typeSymbol.GetMembers().OfType<IPropertySymbol>())
            {
                if (member.GetAttributes().Any(attr => 
                    attr.AttributeClass != null &&
                    (attr.AttributeClass.Name == "ReactiveAttribute" || 
                     attr.AttributeClass.Name == "Reactive")))
                {
                    return true;
                }
            }

            return typeSymbol.BaseType != null && HasReactivePropertiesInHierarchy(typeSymbol.BaseType);
        }

        private static string GetAccessorAccessibility(IMethodSymbol accessor)
        {
            if (accessor == null)
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
                
        private static string GetBackingFieldName(string propertyName)
        {
            return "_" + char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1);
        }

        private static string GetEventArgsFieldName(string propertyName)
        {
            return "_" + char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1) + "ChangedEventArgs";
        }

        private const string AttributeSource = @"using System;

[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
sealed class ReactiveAttribute : Attribute
{
    public ReactiveAttribute() { }
}";
    }
}
