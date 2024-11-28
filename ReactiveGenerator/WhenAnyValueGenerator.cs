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
public class WhenAnyValueGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // No changes to Initialize method
        var classDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: (s, _) => IsCandidateClass(s),
                transform: (ctx, _) => GetClassInfo(ctx))
            .Where(c => c is not null);

        context.RegisterPostInitializationOutput(ctx =>
        {
            ctx.AddSource(
                "PropertyObserver.g.cs",
                SourceText.From(UpdatedPropertyObserverSource, Encoding.UTF8));

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
        // Look for classes that might have reactive properties
        if (node is not ClassDeclarationSyntax classDeclaration)
            return false;

        // Must be partial
        if (!classDeclaration.Modifiers.Any(m => m.ValueText == "partial"))
            return false;

        // Must have property with [Reactive] attribute
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

        // Group classes by type and source file
        var classGroups = classes
            .GroupBy(
                c => (Type: c.Symbol, FilePath: c.Location.SourceTree?.FilePath ?? string.Empty),
                (key, group) => new { TypeSymbol = key.Type, FilePath = key.FilePath },
                new TypeAndPathComparer())
            .ToList();

        // Generate a separate file for each class group
        foreach (var group in classGroups)
        {
            var source = GenerateExtensionsForClass(group.TypeSymbol);
            var sourceFilePath = Path.GetFileNameWithoutExtension(group.FilePath);
            var fileName = $"{group.TypeSymbol.Name}.{sourceFilePath}.WhenAnyValue.g.cs";
            context.AddSource(fileName, SourceText.From(source, Encoding.UTF8));
        }
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

    private static string GenerateExtensionsForClass(INamedTypeSymbol classSymbol)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.ComponentModel;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using ReactiveGenerator.Internal;");
        sb.AppendLine();

        // Add namespace
        var containingNamespace = classSymbol.ContainingNamespace;
        if (!containingNamespace.IsGlobalNamespace)
        {
            sb.AppendLine($"namespace {containingNamespace}");
        }
        else
        {
            sb.AppendLine("namespace Global");
        }

        sb.AppendLine("{");

        // Generate class-specific extension class
        sb.AppendLine($"    public static class {classSymbol.Name}WhenAnyValueExtensions");
        sb.AppendLine("    {");

        var reactiveProperties = classSymbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => p.GetAttributes()
                .Any(a => a.AttributeClass?.Name is "ReactiveAttribute" or "Reactive"));

        foreach (var property in reactiveProperties)
        {
            GenerateWhenAnyValueMethod(sb, classSymbol, property);
            sb.AppendLine();
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static void GenerateWhenAnyValueMethod(
        StringBuilder sb,
        INamedTypeSymbol classSymbol,
        IPropertySymbol property)
    {
        var className = classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var propertyType = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var nullablePropertyType = property.Type.NullableAnnotation == NullableAnnotation.NotAnnotated
            ? propertyType
            : $"{propertyType}?";

        sb.AppendLine($"        public static IObservable<{nullablePropertyType}> WhenAny{property.Name}(");
        sb.AppendLine($"            this {className} source)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (source is null) throw new ArgumentNullException(nameof(source));");
        sb.AppendLine();
        sb.AppendLine($"            return new PropertyObserver<{className}, {nullablePropertyType}>(");
        sb.AppendLine("                source,");
        sb.AppendLine($"                nameof({className}.{property.Name}),");
        sb.AppendLine($"                () => source.{property.Name});");
        sb.AppendLine("        }");
    }


    private const string UpdatedPropertyObserverSource = @"// <auto-generated/>
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
        private readonly WeakEventManager<PropertyChangedEventHandler> _eventManager;
        private readonly PropertyChangedEventHandler _handler;
        private bool _isDisposed;
        private readonly ConcurrentDictionary<IDisposable, byte> _subscriptions;

        public PropertyObserver(TSource source, string propertyName, Func<TProperty> getter)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (propertyName == null) throw new ArgumentNullException(nameof(propertyName));
            if (getter == null) throw new ArgumentNullException(nameof(getter));

            _source = source;
            _propertyName = propertyName;
            _getter = getter;
            _eventManager = new WeakEventManager<PropertyChangedEventHandler>();
            _subscriptions = new ConcurrentDictionary<IDisposable, byte>();
            _handler = HandlePropertyChanged;
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
                    _eventManager.AddEventHandler(_source, ""PropertyChanged"", _handler);
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
                if (subscription is Subscription activeSubscription)
                {
                    try
                    {
                        var observer = activeSubscription.Observer;
                        if (observer != null)
                        {
                            observer.OnNext(_getter());
                        }
                        else
                        {
                            activeSubscription.Dispose();
                        }
                    }
                    catch (Exception ex)
                    {
                        var observer = activeSubscription.Observer;
                        if (observer != null)
                        {
                            observer.OnError(ex);
                        }
                        activeSubscription.Dispose();
                    }
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (!_isDisposed)
                {
                    foreach (var subscription in _subscriptions.Keys)
                    {
                        subscription.Dispose();
                    }
                    _subscriptions.Clear();
                    _eventManager.RemoveEventHandler(_source, ""PropertyChanged"", _handler);
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
                get => _weakObserver.TryGetTarget(out var observer) ? observer : null;
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
        }
    }

    internal sealed class Disposable : IDisposable
    {
        public static readonly IDisposable Empty = new Disposable();
        private Disposable() { }
        public void Dispose() { }
    }
}";

    private const string WeakEventManagerSource = @"
using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ReactiveGenerator.Internal
{
    internal sealed class WeakEventManager<TDelegate> where TDelegate : class, Delegate
    {
        private readonly ConditionalWeakTable<object, EventRegistrationList> _registrations = 
            new ConditionalWeakTable<object, EventRegistrationList>();

        public void AddEventHandler(object source, string eventName, TDelegate handler)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (eventName == null) throw new ArgumentNullException(nameof(eventName));
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            var eventInfo = source.GetType().GetEvent(eventName);
            if (eventInfo == null)
            {
                throw new ArgumentException($""Event '{eventName}' not found on type '{source.GetType()}'."");
            }

            var list = _registrations.GetOrCreateValue(source);
            var registration = new WeakEventRegistration(eventInfo, handler);
            list.Add(registration);

            registration.Subscribe(source);
        }

        public void RemoveEventHandler(object source, string eventName, TDelegate handler)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (eventName == null) throw new ArgumentNullException(nameof(eventName));
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            if (_registrations.TryGetValue(source, out var list))
            {
                list.Remove(eventName, handler);
            }
        }

        private sealed class EventRegistrationList
        {
            private readonly ConcurrentDictionary<string, ConcurrentDictionary<TDelegate, WeakEventRegistration>> _registrations =
                new ConcurrentDictionary<string, ConcurrentDictionary<TDelegate, WeakEventRegistration>>();

            public void Add(WeakEventRegistration registration)
            {
                var eventHandlers = _registrations.GetOrAdd(
                    registration.EventInfo.Name,
                    _ => new ConcurrentDictionary<TDelegate, WeakEventRegistration>());

                eventHandlers[registration.Handler] = registration;
            }

            public void Remove(string eventName, TDelegate handler)
            {
                if (_registrations.TryGetValue(eventName, out var eventHandlers))
                {
                    if (eventHandlers.TryRemove(handler, out var registration))
                    {
                        registration.Unsubscribe();
                    }
                }
            }
        }

        private sealed class WeakEventRegistration
        {
            private readonly WeakReference<TDelegate> _weakDelegate;
            private readonly EventInfo _eventInfo;
            private readonly TDelegate _handler;

            public WeakEventRegistration(EventInfo eventInfo, TDelegate handler)
            {
                _eventInfo = eventInfo;
                _handler = handler;
                _weakDelegate = new WeakReference<TDelegate>(handler);
            }

            public EventInfo EventInfo => _eventInfo;
            public TDelegate Handler => _handler;

            public void Subscribe(object source)
            {
                if (_weakDelegate.TryGetTarget(out var handler))
                {
                    EventInfo.AddEventHandler(source, handler);
                }
            }

            public void Unsubscribe()
            {
                if (_weakDelegate.TryGetTarget(out var handler))
                {
                    _weakDelegate.SetTarget(null);
                }
            }
        }
    }
}";
}
