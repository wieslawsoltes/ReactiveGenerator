using System.Collections.Generic;
using System.Linq;
using System.Text;
using System;
using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ReactiveGenerator;

[Generator]
public class WhenAnyValueGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: (s, _) => IsCandidateClass(s),
                transform: (ctx, _) => GetClassInfo(ctx))
            .Where(c => c is not null);

        context.RegisterPostInitializationOutput(ctx =>
        {
            ctx.AddSource(
                "PropertyObserver.g.cs",
                SourceText.From(PropertyObserverSource, Encoding.UTF8));

            ctx.AddSource(
                "WeakEventManager.g.cs",
                SourceText.From(WeakEventManagerSource, Encoding.UTF8));
        });

        var compilationAndClasses = context.CompilationProvider.Combine(classDeclarations.Collect());

        context.RegisterSourceOutput(
            compilationAndClasses,
            (spc, source) => Execute(source.Left,
                source.Right.Cast<(INamedTypeSymbol Symbol, Location Location)>().ToList(), spc));
    }

    private static bool IsCandidateClass(SyntaxNode node)
    {
        if (node is not ClassDeclarationSyntax classDeclaration)
            return false;

        // Must be partial
        if (!classDeclaration.Modifiers.Any(m => m.ValueText == "partial"))
            return false;

        // Has class-level [Reactive] attribute
        if (classDeclaration.AttributeLists.Any(al =>
                al.Attributes.Any(a => a.Name.ToString() is "Reactive" or "ReactiveAttribute")))
            return true;

        // Or any property has [Reactive] attribute
        return classDeclaration.Members
            .OfType<PropertyDeclarationSyntax>()
            .Any(p => p.AttributeLists.Count > 0 &&
                      p.AttributeLists.Any(al =>
                          al.Attributes.Any(a =>
                              a.Name.ToString() is "Reactive" or "ReactiveAttribute")));
    }

    private static (INamedTypeSymbol Symbol, Location Location)? GetClassInfo(GeneratorSyntaxContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var symbol = context.SemanticModel.GetDeclaredSymbol(classDeclaration);
        return symbol != null ? (symbol, classDeclaration.GetLocation()) : null;
    }

    private static void Execute(
        Compilation compilation,
        List<(INamedTypeSymbol Symbol, Location Location)> classes,
        SourceProductionContext context)
    {
        if (classes.Count == 0) return;

        var processedTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var reactiveClasses = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        // Identify classes with [Reactive] attribute
        foreach (var (typeSymbol, _) in classes)
        {
            if (typeSymbol.GetAttributes().Any(attr =>
                    attr.AttributeClass?.Name is "ReactiveAttribute" or "Reactive"))
            {
                reactiveClasses.Add(typeSymbol);
            }
        }

        foreach (var (typeSymbol, _) in classes)
        {
            if (processedTypes.Add(typeSymbol))
            {
                var properties = GetReactiveProperties(typeSymbol, reactiveClasses);
                if (properties.Any())
                {
                    var source = GenerateExtensionsForClass(typeSymbol, properties);
                    var fullTypeName = typeSymbol.ToDisplayString(new SymbolDisplayFormat(
                        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                        genericsOptions: SymbolDisplayGenericsOptions.None,
                        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.None));

                    var fileName = $"{fullTypeName}.WhenAnyValue.g.cs";
                    context.AddSource(fileName, SourceText.From(source, Encoding.UTF8));
                }
            }
        }
    }

    private static IEnumerable<IPropertySymbol> GetReactiveProperties(
        INamedTypeSymbol typeSymbol,
        HashSet<INamedTypeSymbol> reactiveClasses)
    {
        var isReactiveClass = reactiveClasses.Contains(typeSymbol);

        return typeSymbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p =>
            {
                var hasIgnoreAttribute = p.GetAttributes()
                    .Any(a => a.AttributeClass?.Name is "IgnoreReactiveAttribute" or "IgnoreReactive");
                if (hasIgnoreAttribute)
                    return false;

                var hasReactiveAttribute = p.GetAttributes()
                    .Any(a => a.AttributeClass?.Name is "ReactiveAttribute" or "Reactive");
                return hasReactiveAttribute || isReactiveClass;
            });
    }

    private static bool IsTypeAccessible(INamedTypeSymbol typeSymbol)
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

    private static string FormatTypeNameForXmlDoc(ITypeSymbol type)
    {
        var format = new SymbolDisplayFormat(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);
        return type.ToDisplayString(format).Replace("<", "{").Replace(">", "}");
    }

    private static string GenerateExtensionsForClass(
        INamedTypeSymbol classSymbol,
        IEnumerable<IPropertySymbol> properties)
    {
        if (!IsTypeAccessible(classSymbol))
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.ComponentModel;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using ReactiveGenerator.Internal;");
        sb.AppendLine();

        var namespaceName = classSymbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : classSymbol.ContainingNamespace.ToDisplayString();

        if (namespaceName != null)
        {
            sb.AppendLine($"namespace {namespaceName}");
            sb.AppendLine("{");
        }

        var indent = namespaceName != null ? "    " : "";

        var extensionClassName = classSymbol.ToDisplayString(new SymbolDisplayFormat(
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                genericsOptions: SymbolDisplayGenericsOptions.None,
                miscellaneousOptions: SymbolDisplayMiscellaneousOptions.None))
            .Replace(".", "_") + "Extensions";

        var accessibility = classSymbol.DeclaredAccessibility == Accessibility.Internal ? "internal" : "public";
        sb.AppendLine($"{indent}{accessibility} static class {extensionClassName}");
        sb.AppendLine($"{indent}{{");

        var propertiesList = properties.ToList();
        var lastProperty = propertiesList.LastOrDefault();

        foreach (var property in propertiesList)
        {
            GenerateWhenAnyValueMethod(sb, classSymbol, property, indent + "    ");

            if (!SymbolEqualityComparer.Default.Equals(property, lastProperty))
            {
                sb.AppendLine();
            }
        }

        sb.AppendLine($"{indent}}}");

        if (namespaceName != null)
        {
            sb.AppendLine("}");
        }

        return sb.ToString();
    }

    private static void GenerateWhenAnyValueMethod(
        StringBuilder sb,
        INamedTypeSymbol classSymbol,
        IPropertySymbol property,
        string indent)
    {
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
                else if (typeParam.HasValueTypeConstraint)
                    paramConstraints.Add("struct");

                foreach (var constraintType in typeParam.ConstraintTypes)
                {
                    paramConstraints.Add(constraintType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                }

                if (typeParam.HasConstructorConstraint)
                    paramConstraints.Add("new()");

                if (paramConstraints.Count > 0)
                {
                    constraints.Add($"where {typeParam.Name} : {string.Join(", ", paramConstraints)}");
                }
            }

            if (constraints.Count > 0)
            {
                typeConstraints = " " + string.Join(" ", constraints);
            }
        }

        var className = classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var propertyType = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var isAlreadyNullable = property.Type.OriginalDefinition?.SpecialType == SpecialType.System_Nullable_T;
        var nullablePropertyType = isAlreadyNullable
            ? propertyType
            : (property.Type.NullableAnnotation == NullableAnnotation.NotAnnotated
                ? propertyType
                : $"{propertyType}?");

        var methodAccessibility = classSymbol.DeclaredAccessibility == Accessibility.Internal ? "internal" : "public";

        if (!string.IsNullOrEmpty(typeParameters))
        {
            sb.AppendLine(
                $"{indent}{methodAccessibility} static IObservable<{nullablePropertyType}> WhenAny{property.Name}{typeParameters}(");
        }
        else
        {
            sb.AppendLine(
                $"{indent}{methodAccessibility} static IObservable<{nullablePropertyType}> WhenAny{property.Name}(");
        }

        sb.AppendLine($"{indent}    this {className} source){typeConstraints}");
        sb.AppendLine($"{indent}{{");
        sb.AppendLine($"{indent}    if (source is null) throw new ArgumentNullException(nameof(source));");
        sb.AppendLine();
        sb.AppendLine($"{indent}    return new PropertyObserver<{className}, {nullablePropertyType}>(");
        sb.AppendLine($"{indent}        source,");
        sb.AppendLine($"{indent}        \"{property.Name}\",");
        sb.AppendLine($"{indent}        () => source.{property.Name});");
        sb.AppendLine($"{indent}}}");
    }

    private const string PropertyObserverSource = @"// <auto-generated/>
#nullable enable

using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Threading;

namespace ReactiveGenerator.Internal
{
    internal sealed class PropertyObserver<TSource, TProperty> : IObservable<TProperty>, IDisposable
        where TSource : INotifyPropertyChanged
    {
        private readonly object _gate = new object();
        private readonly TSource _source;
        private readonly string _propertyName;
        private readonly Func<TProperty> _getter;
        private bool _isDisposed;
        private readonly ConcurrentDictionary<Subscription, byte> _subscriptions;
        private readonly PropertyChangedEventHandler _handler;
        private readonly WeakEventManager<PropertyChangedEventHandler> _weakEventManager;

        public PropertyObserver(TSource source, string propertyName, Func<TProperty> getter)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (propertyName == null) throw new ArgumentNullException(nameof(propertyName));
            if (getter == null) throw new ArgumentNullException(nameof(getter));

            _source = source;
            _propertyName = propertyName;
            _getter = getter;
            _subscriptions = new ConcurrentDictionary<Subscription, byte>();
            _handler = HandlePropertyChanged;
            _weakEventManager = new WeakEventManager<PropertyChangedEventHandler>(
                (obj, h) => ((INotifyPropertyChanged)obj).PropertyChanged += h,
                (obj, h) => ((INotifyPropertyChanged)obj).PropertyChanged -= h
            );
        }

        public IDisposable Subscribe(IObserver<TProperty> observer)
        {
            if (observer == null) throw new ArgumentNullException(nameof(observer));

            lock (_gate)
            {
                if (_isDisposed)
                {
                    observer.OnCompleted();
                    return Disposable.Empty;
                }

                var subscription = new Subscription(this, observer);
                _subscriptions.TryAdd(subscription, 0);

                try
                {
                    observer.OnNext(_getter());
                    _weakEventManager.AddEventHandler(_source, _handler);
                }
                catch (Exception ex)
                {
                    observer.OnError(ex);
                    subscription.Dispose();
                    return Disposable.Empty;
                }

                return subscription;
            }
        }

        private void HandlePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != _propertyName && !string.IsNullOrEmpty(e.PropertyName))
                return;

            foreach (var subscription in _subscriptions.Keys)
            {
                var obs = subscription.Observer;
                if (obs == null)
                {
                    subscription.Dispose();
                    continue;
                }

                try
                {
                    obs.OnNext(_getter());
                }
                catch (Exception ex)
                {
                    obs.OnError(ex);
                    subscription.Dispose();
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (!_isDisposed)
                {
                    _weakEventManager.RemoveEventHandler(_source, _handler);
                    foreach (var subscription in _subscriptions.Keys)
                    {
                        subscription.DisposeInternal();
                    }
                    _subscriptions.Clear();
                    _isDisposed = true;
                }
            }
        }

        private sealed class Subscription : IDisposable
        {
            private readonly PropertyObserver<TSource, TProperty> _parent;
            private readonly WeakReference<IObserver<TProperty>> _weakObserver;
            private int _disposed;

            public Subscription(PropertyObserver<TSource, TProperty> parent, IObserver<TProperty> observer)
            {
                _parent = parent;
                _weakObserver = new WeakReference<IObserver<TProperty>>(observer);
            }

            public IObserver<TProperty>? Observer
            {
                get => _weakObserver.TryGetTarget(out var obs) ? obs : null;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    lock (_parent._gate)
                    {
                        if (!_parent._isDisposed)
                        {
                            _parent._subscriptions.TryRemove(this, out _);
                            if (_parent._subscriptions.IsEmpty)
                            {
                                _parent.Dispose();
                            }
                        }
                    }
                }
            }

            public void DisposeInternal()
            {
                Interlocked.Exchange(ref _disposed, 1);
            }
        }

        private sealed class Disposable : IDisposable
        {
            public static readonly IDisposable Empty = new Disposable();
            private Disposable() { }
            public void Dispose() { }
        }
    }
}";

    private const string WeakEventManagerSource = @"// <auto-generated/>
#nullable enable

using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace ReactiveGenerator.Internal
{
    internal sealed class WeakEventManager<TDelegate> where TDelegate : class, Delegate
    {
        private readonly ConditionalWeakTable<object, EventRegistrationList> _registrations =
            new ConditionalWeakTable<object, EventRegistrationList>();

        private readonly Action<object, TDelegate> _addHandler;
        private readonly Action<object, TDelegate> _removeHandler;

        public WeakEventManager(Action<object, TDelegate> addHandler, Action<object, TDelegate> removeHandler)
        {
            _addHandler = addHandler ?? throw new ArgumentNullException(nameof(addHandler));
            _removeHandler = removeHandler ?? throw new ArgumentNullException(nameof(removeHandler));
        }

        public void AddEventHandler(object source, TDelegate handler)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            var list = _registrations.GetOrCreateValue(source);
            var registration = new WeakEventRegistration(source, handler, _removeHandler);
            list.Add(registration);
            _addHandler(source, handler);
        }

        public void RemoveEventHandler(object source, TDelegate handler)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            if (_registrations.TryGetValue(source, out var list))
            {
                list.Remove(handler);
            }
        }

        private sealed class EventRegistrationList
        {
            private readonly ConcurrentDictionary<TDelegate, WeakEventRegistration> _registrations =
                new ConcurrentDictionary<TDelegate, WeakEventRegistration>();

            public void Add(WeakEventRegistration registration)
            {
                _registrations[registration.Handler] = registration;
            }

            public void Remove(TDelegate handler)
            {
                if (_registrations.TryRemove(handler, out var registration))
                {
                    registration.Unsubscribe();
                }
            }
        }

        private sealed class WeakEventRegistration
        {
            private readonly WeakReference _sourceReference;
            private readonly WeakReference<TDelegate> _handlerReference;
            private readonly Action<object, TDelegate> _removeHandler;

            public WeakEventRegistration(object source, TDelegate handler, Action<object, TDelegate> removeHandler)
            {
                _sourceReference = new WeakReference(source);
                _handlerReference = new WeakReference<TDelegate>(handler);
                _removeHandler = removeHandler;
            }

            public TDelegate Handler
            {
                get
                {
                    _handlerReference.TryGetTarget(out var handler);
                    return handler!;
                }
            }

            public void Unsubscribe()
            {
                if (_handlerReference.TryGetTarget(out var handler) && _sourceReference.Target is object source)
                {
                    _removeHandler(source, handler);
                }
            }
        }
    }
}";
}
