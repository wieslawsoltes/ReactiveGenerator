using System.Collections.Generic;
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
            context.RegisterPostInitializationOutput(ctx =>
            {
                ctx.AddSource("ReactiveAttribute.g.cs", SourceText.From(AttributeSource, Encoding.UTF8));
            });

            var propertyDeclarations = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: (s, _) => IsCandidateProperty(s),
                    transform: (ctx, _) => GetSemanticTarget(ctx))
                .Where(m => m is not null);

            var compilationAndProperties = context.CompilationProvider
                .Combine(propertyDeclarations.Collect());

            context.RegisterSourceOutput(
                compilationAndProperties,
                (spc, source) => Execute(source.Left, source.Right.Cast<IPropertySymbol>().ToList(), spc));
        }

        private static bool IsCandidateProperty(SyntaxNode node)
        {
            if (node is not PropertyDeclarationSyntax propertyDeclaration)
                return false;

            if (!propertyDeclaration.Modifiers.Any(m => m.ValueText == "partial"))
                return false;

            return propertyDeclaration.AttributeLists.Count > 0;
        }

        private static IPropertySymbol? GetSemanticTarget(GeneratorSyntaxContext context)
        {
            if (context.Node is not PropertyDeclarationSyntax propertyDeclaration)
                return null;

            var semanticModel = context.SemanticModel;

            foreach (var attributeList in propertyDeclaration.AttributeLists)
            {
                foreach (var attribute in attributeList.Attributes)
                {
                    var name = attribute.Name.ToString();
                    if (name is "Reactive" or "ReactiveAttribute")
                    {
                        var symbol = semanticModel.GetDeclaredSymbol(propertyDeclaration);
                        return symbol;
                    }
                }
            }

            return null;
        }

        // Rest of the class implementation unchanged...
        private static bool InheritsFromReactiveObject(INamedTypeSymbol typeSymbol)
        {
            var current = typeSymbol;
            while (current is not null)
            {
                if (current.Name == "ReactiveObject")
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

        private static void Execute(
            Compilation compilation,
            List<IPropertySymbol> properties,
            SourceProductionContext context)
        {
            if (properties.Count == 0)
                return;

            var propertyGroups = properties
                .GroupBy(p => p.ContainingType, SymbolEqualityComparer.Default)
                .ToList();

            var processedTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

            foreach (var group in propertyGroups)
            {
                if (group.Key is not INamedTypeSymbol typeSymbol) continue;

                var baseType = FindFirstTypeNeedingINPC(compilation, typeSymbol);
                if (baseType is not null && !processedTypes.Contains(baseType))
                {
                    var source = GenerateClassSource(baseType, properties, implementInpc: true);
                    if (!string.IsNullOrEmpty(source))
                    {
                        context.AddSource(
                            $"{baseType.Name}_ReactiveProperties.g.cs",
                            SourceText.From(source, Encoding.UTF8));
                        processedTypes.Add(baseType);
                    }
                }
            }

            foreach (var group in propertyGroups)
            {
                if (group.Key is not INamedTypeSymbol typeSymbol || processedTypes.Contains(typeSymbol)) continue;

                var source = GenerateClassSource(typeSymbol, properties, implementInpc: false);
                if (!string.IsNullOrEmpty(source))
                {
                    context.AddSource(
                        $"{typeSymbol.Name}_ReactiveProperties.g.cs",
                        SourceText.From(source, Encoding.UTF8));
                }
            }
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
        private static string GenerateClassSource(
            INamedTypeSymbol classSymbol,
            List<IPropertySymbol> allProperties,
            bool implementInpc)
        {
            var typeProperties = allProperties.Where(p => 
                SymbolEqualityComparer.Default.Equals(p.ContainingType, classSymbol)).ToList();

            if (!typeProperties.Any())
                return string.Empty;

            var isReactiveObject = InheritsFromReactiveObject(classSymbol);
            var namespaceName = classSymbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : classSymbol.ContainingNamespace.ToDisplayString();

            var sb = new StringBuilder();
            
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

            if (implementInpc && !isReactiveObject)
            {
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
                sb.AppendLine();
            }

            foreach (var property in typeProperties)
            {
                var propertyName = property.Name;
                var propertyType = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var backingFieldName = GetBackingFieldName(propertyName);
                
                sb.AppendLine($"        private {propertyType} {backingFieldName};");
                sb.AppendLine();

                var propertyAccessibility = property.DeclaredAccessibility.ToString().ToLowerInvariant();

                if (isReactiveObject)
                {
                    var getterAccessibility = GetAccessorAccessibility(property.GetMethod);
                    var setterAccessibility = GetAccessorAccessibility(property.SetMethod);
                    var getterModifier = getterAccessibility != propertyAccessibility ? $"{getterAccessibility} " : "";
                    var setterModifier = setterAccessibility != propertyAccessibility ? $"{setterAccessibility} " : "";

                    sb.AppendLine($"        {propertyAccessibility} partial {propertyType} {propertyName}");
                    sb.AppendLine("        {");
                    sb.AppendLine($"            {getterModifier}get => {backingFieldName};");
                    if (property.SetMethod != null)
                    {
                        sb.AppendLine($"            {setterModifier}set => this.RaiseAndSetIfChanged(ref {backingFieldName}, value);");
                    }
                    sb.AppendLine("        }");
                }
                else
                {
                    var getterAccessibility = GetAccessorAccessibility(property.GetMethod);
                    var setterAccessibility = GetAccessorAccessibility(property.SetMethod);
                    var eventArgsFieldName = GetEventArgsFieldName(propertyName);

                    sb.AppendLine($"        {propertyAccessibility} partial {propertyType} {propertyName}");
                    sb.AppendLine("        {");

                    var getterModifier = getterAccessibility != propertyAccessibility ? $"{getterAccessibility} " : "";
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

                    sb.AppendLine("        }");
                }
                sb.AppendLine();
            }

            sb.AppendLine("    }");

            if (namespaceName != null)
            {
                sb.AppendLine("}");
            }

            return sb.ToString();
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
