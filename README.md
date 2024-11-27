# ReactiveGenerator

[![CI](https://github.com/wieslawsoltes/ReactiveGenerator/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/ReactiveGenerator/actions/workflows/build.yml)
[![NuGet](https://img.shields.io/nuget/v/ReactiveGenerator.svg)](https://www.nuget.org/packages/ReactiveGenerator)
[![NuGet](https://img.shields.io/nuget/dt/ReactiveGenerator.svg)](https://www.nuget.org/packages/ReactiveGenerator)

A C# source generator that automatically implements property change notifications using either standard `INotifyPropertyChanged` or ReactiveUI patterns. It provides efficient code generation for properties with full type safety and design-time support.

## Features

### Core Features
- Automatic `INotifyPropertyChanged` implementation
- Support for ReactiveUI's `ReactiveObject` pattern
- Automatic `WhenAnyValue` observable generation for reactive properties
- Support for modern C# field keyword and legacy backing fields
- Full nullable reference type support
- Inheritance-aware property generation
- Flexible property access modifiers
- Cached `PropertyChangedEventArgs` for performance optimization

### Performance Optimizations
- Static caching of PropertyChangedEventArgs instances
- Efficient property change detection using Equals
- Minimal memory allocation during updates
- Zero-overhead property access for unchanged values
- Optimized WhenAnyValue observable implementation with weak event handling
- Thread-safe event management

## Installation

```bash
dotnet add package ReactiveGenerator
```

### Prerequisites
- .NET 9.0 or later
- C# 13.0 or later
- `<LangVersion>preview</LangVersion>` in project file
- Visual Studio 2022 or compatible IDE

## Basic Usage

### Standard INPC Implementation

```csharp
public partial class Person
{
    [Reactive]
    public partial string FirstName { get; set; }

    [Reactive]
    public partial string LastName { get; set; }
}
```

### ReactiveUI Integration

```csharp
public partial class ViewModel : ReactiveObject
{
    [Reactive]
    public partial string SearchText { get; set; }

    [Reactive]
    public partial bool IsLoading { get; set; }

    public ViewModel()
    {
        // Use generated WhenAnyValue extensions
        this.WhenAnySearchText()
            .Subscribe(text => PerformSearch(text));
    }
}
```

## Configuration

### MSBuild Properties

```xml
<PropertyGroup>
    <!-- Optional: Use explicit backing fields instead of field keyword -->
    <UseBackingFields>true</UseBackingFields>
</PropertyGroup>
```

## Implementation Details

### Generated Types

The generator produces several key types:

1. `PropertyObserver<TSource, TProperty>`: Handles property observation with memory-safe subscriptions
2. `WeakEventManager<TDelegate>`: Provides thread-safe weak event handling
3. Extension methods for each reactive property (e.g., `WhenAnyPropertyName`)

### Generated Code Structure

The generator produces two main types of code:

1. Property Change Notifications:
    - INPC implementation for classes not inheriting from ReactiveObject
    - Efficient property setters with change detection
    - Cached PropertyChangedEventArgs instances

2. WhenAnyValue Observables:
    - Type-safe property observation methods
    - Weak event handling to prevent memory leaks
    - Thread-safe subscription management

### Property Implementation Modes

#### Modern Mode (Default)
Uses C# field keyword:

```csharp
public partial string Property
{
    get => field;
    set
    {
        if (!Equals(field, value))
        {
            field = value;
            OnPropertyChanged(_propertyChangedEventArgs);
        }
    }
}
```

#### Legacy Mode
Uses explicit backing fields:

```csharp
private string _property;
public partial string Property
{
    get => _property;
    set
    {
        if (!Equals(_property, value))
        {
            _property = value;
            OnPropertyChanged(_propertyChangedEventArgs);
        }
    }
}
```

## Advanced Features

### Weak Event Management

The generator includes a sophisticated weak event system that:
- Prevents memory leaks in long-lived observable subscriptions
- Automatically cleans up when observers are garbage collected
- Maintains thread safety for event handling

### Inheritance Support

The generator is fully aware of inheritance hierarchies:
- Correctly implements INPC only once in the inheritance chain
- Properly handles virtual and override properties
- Supports mixed INPC and ReactiveUI inheritance scenarios

### Performance Optimizations

1. Event Args Caching:
```csharp
private static readonly PropertyChangedEventArgs _propertyChangedEventArgs = 
    new PropertyChangedEventArgs(nameof(Property));
```

2. Efficient Change Detection:
```csharp
if (!Equals(field, value))
{
    field = value;
    OnPropertyChanged(_propertyChangedEventArgs);
}
```

3. Weak Event References:
```csharp
internal sealed class WeakEventManager<TDelegate>
{
    private readonly ConditionalWeakTable<object, EventRegistrationList> _registrations;
    // Implementation details...
}
```

## Best Practices

1. Always mark reactive classes and properties as `partial`
2. Use `IDisposable` for proper subscription cleanup
3. Consider thread safety when using observables
4. Implement `IEquatable<T>` for complex property types
5. Use the generated WhenAny methods instead of magic strings

## Common Issues and Solutions

### Missing Partial Declarations
```csharp
// ❌ Wrong
public class Example
{
    [Reactive]
    public string Property { get; set; }
}

// ✅ Correct
public partial class Example
{
    [Reactive]
    public partial string Property { get; set; }
}
```

### ReactiveUI Base Class
```csharp
// ❌ Wrong: Missing ReactiveObject base
public partial class Example
{
    [Reactive]
    public partial string Property { get; set; }
}

// ✅ Correct
public partial class Example : ReactiveObject
{
    [Reactive]
    public partial string Property { get; set; }
}
```

## License

ReactiveGenerator is licensed under the MIT license.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request. For major changes, please open an issue first to discuss what you would like to change.
