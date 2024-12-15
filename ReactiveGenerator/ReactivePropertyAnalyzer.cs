using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeActions;
using System.Composition;
using Microsoft.CodeAnalysis.Text;
using System.Linq;

namespace ReactiveAnalyzer
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class ReactivePropertyAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "REACTIVE001";
        private const string Title = "Property can use [Reactive] attribute";
        private const string MessageFormat = "Property '{0}' can be simplified using [Reactive] attribute";
        private const string Description = "Properties using RaiseAndSetIfChanged can be simplified using the [Reactive] attribute.";
        private const string Category = "Usage";

        private static readonly DiagnosticDescriptor Rule = new(
            DiagnosticId,
            Title,
            MessageFormat,
            Category,
            DiagnosticSeverity.Info,
            isEnabledByDefault: true,
            description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeProperty, SyntaxKind.PropertyDeclaration);
        }

        private void AnalyzeProperty(SyntaxNodeAnalysisContext context)
        {
            var propertyDeclaration = (PropertyDeclarationSyntax)context.Node;

            // Check if property has both getter and setter
            if (!HasGetterAndSetter(propertyDeclaration))
                return;

            // Check if the setter uses RaiseAndSetIfChanged
            if (!UsesRaiseAndSetIfChanged(propertyDeclaration))
                return;

            // Report diagnostic
            var diagnostic = Diagnostic.Create(
                Rule,
                propertyDeclaration.GetLocation(),
                propertyDeclaration.Identifier.Text);
            
            context.ReportDiagnostic(diagnostic);
        }

        private bool HasGetterAndSetter(PropertyDeclarationSyntax property)
        {
            if (property.AccessorList == null)
                return false;

            var accessors = property.AccessorList.Accessors;
            return accessors.Any(a => a.IsKind(SyntaxKind.GetAccessorDeclaration)) &&
                   accessors.Any(a => a.IsKind(SyntaxKind.SetAccessorDeclaration));
        }

        private bool UsesRaiseAndSetIfChanged(PropertyDeclarationSyntax property)
        {
            var setter = property.AccessorList?.Accessors
                .FirstOrDefault(a => a.IsKind(SyntaxKind.SetAccessorDeclaration));

            if (setter?.Body == null && setter?.ExpressionBody == null)
                return false;

            // Check for this.RaiseAndSetIfChanged(...) pattern
            var setterText = setter.ToString();
            return setterText.Contains("RaiseAndSetIfChanged");
        }
    }

    [ExportCodeFixProvider(LanguageNames.CSharp), Shared]
    public class ReactivePropertyCodeFixProvider : CodeFixProvider
    {
        public sealed override ImmutableArray<string> FixableDiagnosticIds =>
            ImmutableArray.Create(ReactivePropertyAnalyzer.DiagnosticId);

        public sealed override FixAllProvider GetFixAllProvider() =>
            WellKnownFixAllProviders.BatchFixer;

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            if (root == null) return;

            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;
            var propertyDeclaration = root.FindToken(diagnosticSpan.Start)
                .Parent?.AncestorsAndSelf()
                .OfType<PropertyDeclarationSyntax>()
                .First();

            if (propertyDeclaration == null) return;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Convert to [Reactive] property",
                    createChangedDocument: c => ConvertToReactivePropertyAsync(context.Document, propertyDeclaration, c),
                    equivalenceKey: nameof(ReactivePropertyCodeFixProvider)),
                diagnostic);
        }

        private async Task<Document> ConvertToReactivePropertyAsync(
            Document document,
            PropertyDeclarationSyntax propertyDeclaration,
            CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (root == null) return document;

            // Create the new property with [Reactive] attribute and partial modifier
            var newProperty = propertyDeclaration
                .WithAttributeLists(
                    SyntaxFactory.SingletonList(
                        SyntaxFactory.AttributeList(
                            SyntaxFactory.SingletonSeparatedList(
                                SyntaxFactory.Attribute(SyntaxFactory.IdentifierName("Reactive"))))))
                .WithModifiers(
                    propertyDeclaration.Modifiers.Add(SyntaxFactory.Token(SyntaxKind.PartialKeyword)))
                .WithAccessorList(
                    SyntaxFactory.AccessorList(
                        SyntaxFactory.List(new[]
                        {
                            SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
                            SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                        })));

            // Replace old property with new one
            var newRoot = root.ReplaceNode(propertyDeclaration, newProperty);

            // Find and remove backing field
            var classDeclaration = propertyDeclaration.Parent as ClassDeclarationSyntax;
            if (classDeclaration != null)
            {
                var backingFieldName = "_" + char.ToLower(propertyDeclaration.Identifier.Text[0]) + 
                    propertyDeclaration.Identifier.Text.Substring(1);
                var backingField = classDeclaration.Members
                    .OfType<FieldDeclarationSyntax>()
                    .FirstOrDefault(f => f.Declaration.Variables
                        .Any(v => v.Identifier.Text == backingFieldName));

                if (backingField != null)
                {
                    newRoot = newRoot.RemoveNode(backingField, SyntaxRemoveOptions.KeepNoTrivia);
                }
            }

            return document.WithSyntaxRoot(newRoot);
        }
    }
}
