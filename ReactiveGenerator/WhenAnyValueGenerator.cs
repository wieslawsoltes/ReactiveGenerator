using System.Collections.Generic;
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
        // Look for classes that might have reactive properties
        if (node is not ClassDeclarationSyntax classDeclaration)
            return false;

        // Must be partial
        if (!classDeclaration.Modifiers.Any(m => m.ValueText == "partial"))
            return false;

        // Has class-level [Reactive] attribute
        if (classDeclaration.AttributeLists.Any(al =>
                al.Attributes.Any(a => a.Name.ToString() is "Reactive" or "ReactiveAttribute")))
            return true;

        // Or has any property with [Reactive] attribute
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

        // Use a HashSet to track distinct types
        var processedTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var reactiveClasses = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        // First pass: identify classes with [Reactive] attribute
        foreach (var (typeSymbol, _) in classes)
        {
            if (typeSymbol.GetAttributes().Any(attr => 
                    attr.AttributeClass?.Name is "ReactiveAttribute" or "Reactive"))
            {
                reactiveClasses.Add(typeSymbol);
            }
        }

        // Process each class, skipping duplicates
        foreach (var (typeSymbol, _) in classes)
        {
            // Only process each type once
            if (processedTypes.Add(typeSymbol))
            {
                var properties = GetReactiveProperties(typeSymbol, reactiveClasses);
                if (properties.Any())
                {
                    var source = GenerateExtensionsForClass(typeSymbol, properties);
                    var fileName = $"{typeSymbol.Name}.WhenAnyValue.g.cs";
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
                // Check for [IgnoreReactive] attribute
                var hasIgnoreAttribute = p.GetAttributes()
                    .Any(a => a.AttributeClass?.Name is "IgnoreReactiveAttribute" or "IgnoreReactive");

                if (hasIgnoreAttribute)
                    return false;

                // Check for explicit [Reactive] attribute
                var hasReactiveAttribute = p.GetAttributes()
                    .Any(a => a.AttributeClass?.Name is "ReactiveAttribute" or "Reactive");

                // Include if:
                // 1. Has explicit [Reactive] attribute, or
                // 2. Class has [Reactive] attribute and property doesn't have [IgnoreReactive]
                return hasReactiveAttribute || isReactiveClass;
            });
    }
    
    private static string GenerateExtensionsForClass(
        INamedTypeSymbol classSymbol,
        IEnumerable<IPropertySymbol> properties)
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
        // Add class XML documentation
        sb.AppendLine("    /// <summary>");
        sb.AppendLine($"    /// Provides extension methods for observing changes to properties of <see cref=\"{classSymbol.Name}\"/>.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine($"    public static class {classSymbol.Name}WhenAnyValueExtensions");
        sb.AppendLine("    {");

        // Generate methods for each property
        var propertiesList = properties.ToList();
        var lastProperty = propertiesList.LastOrDefault();

        foreach (var property in propertiesList)
        {
            GenerateWhenAnyValueMethod(sb, classSymbol, property);
            
            // Only add newline if not the last property
            if (!SymbolEqualityComparer.Default.Equals(property, lastProperty))
            {
                sb.AppendLine();
            }
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

    // For XML documentation, use minimal format without namespace
    var minimalFormat = new SymbolDisplayFormat(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    var xmlClassName = classSymbol.ToDisplayString(minimalFormat);
    // Escape angle brackets for XML cref attribute
    var xmlPropertyType = property.Type.ToDisplayString(minimalFormat).Replace("<", "{").Replace(">", "}");
    
    // Add XML documentation comments for the method
    sb.AppendLine("        /// <summary>");
    sb.AppendLine($"        /// Observes changes to the <see cref=\"{xmlClassName}.{property.Name}\"/> property.");
    sb.AppendLine("        /// </summary>");
    sb.AppendLine("        /// <param name=\"source\">The source object.</param>");
    sb.AppendLine($"        /// <returns>An observable sequence of <see cref=\"{xmlPropertyType}\"/> values.</returns>");
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

    private const string PropertyObserverSource = @"// <auto-generated/>
#nullable enable

using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Threading;

namespace ReactiveGenerator.Internal
{
    /// <summary>
    /// Observes property changes on a source object and notifies subscribers.
    /// </summary>
    /// <typeparam name=""TSource"">The type of the source object.</typeparam>
    /// <typeparam name=""TProperty"">The type of the property being observed.</typeparam>
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

        /// <summary>
        /// Initializes a new instance of the <see cref=""PropertyObserver{TSource, TProperty}""/> class.
        /// </summary>
        /// <param name=""source"">The source object.</param>
        /// <param name=""propertyName"">The name of the property to observe.</param>
        /// <param name=""getter"">A function to get the property value.</param>
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

        /// <summary>
        /// Subscribes an observer to receive notifications.
        /// </summary>
        /// <param name=""observer"">The observer to subscribe.</param>
        /// <returns>A disposable to unsubscribe the observer.</returns>
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

        /// <summary>
        /// Disposes the observer and unsubscribes all observers.
        /// </summary>
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

            /// <summary>
            /// Gets the observer if it is still alive.
            /// </summary>
            public IObserver<TProperty>? Observer
            {
                get => _weakObserver.TryGetTarget(out var observer) ? observer : null;
            }

            /// <summary>
            /// Disposes the subscription and removes it from the parent observer.
            /// </summary>
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

    /// <summary>
    /// Represents a disposable resource that does nothing when disposed.
    /// </summary>
    internal sealed class Disposable : IDisposable
    {
        /// <summary>
        /// Gets a disposable that does nothing when disposed.
        /// </summary>
        public static readonly IDisposable Empty = new Disposable();

        private Disposable() { }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose() { }
    }
}";

    private const string WeakEventManagerSource = @"// <auto-generated/>
#nullable enable

using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ReactiveGenerator.Internal
{
    /// <summary>
    /// Manages weak event subscriptions to avoid memory leaks.
    /// </summary>
    /// <typeparam name=""TDelegate"">The type of the event handler delegate.</typeparam>
    internal sealed class WeakEventManager<TDelegate> where TDelegate : class, Delegate
    {
        private readonly ConditionalWeakTable<object, EventRegistrationList> _registrations =
            new ConditionalWeakTable<object, EventRegistrationList>();

        /// <summary>
        /// Adds a weak event handler to the specified event on the source object.
        /// </summary>
        /// <param name=""source"">The source object.</param>
        /// <param name=""eventName"">The name of the event.</param>
        /// <param name=""handler"">The event handler.</param>
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
            var registration = new WeakEventRegistration(source, eventInfo, handler);
            list.Add(registration);
        }

        /// <summary>
        /// Removes a weak event handler from the specified event on the source object.
        /// </summary>
        /// <param name=""source"">The source object.</param>
        /// <param name=""eventName"">The name of the event.</param>
        /// <param name=""handler"">The event handler.</param>
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
            private readonly WeakReference _sourceReference;
            private readonly WeakReference<TDelegate> _handlerReference;
            private readonly EventInfo _eventInfo;

            public WeakEventRegistration(object source, EventInfo eventInfo, TDelegate handler)
            {
                _sourceReference = new WeakReference(source);
                _eventInfo = eventInfo;
                _handlerReference = new WeakReference<TDelegate>(handler);

                Subscribe();
            }

            /// <summary>
            /// Gets the event information for the event being managed.
            /// </summary>
            public EventInfo EventInfo => _eventInfo;

            /// <summary>
            /// Gets the event handler delegate.
            /// </summary>
            public TDelegate Handler
            {
                get
                {
                    _handlerReference.TryGetTarget(out var handler);
                    return handler!;
                }
            }

            private void Subscribe()
            {
                if (_sourceReference.Target is object source &&
                    _handlerReference.TryGetTarget(out var handler))
                {
                    _eventInfo.AddEventHandler(source, handler);
                }
            }

            /// <summary>
            /// Unsubscribes the handler from the event.
            /// </summary>
            public void Unsubscribe()
            {
                if (_sourceReference.Target is object source &&
                    _handlerReference.TryGetTarget(out var handler))
                {
                    _eventInfo.RemoveEventHandler(source, handler);
                }
            }
        }
    }
}";
}
