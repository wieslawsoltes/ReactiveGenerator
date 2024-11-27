# ReactiveGenerator

[![CI](https://github.com/wieslawsoltes/ReactiveGenerator/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/ReactiveGenerator/actions/workflows/build.yml)
[![NuGet](https://img.shields.io/nuget/v/ReactiveGenerator.svg)](https://www.nuget.org/packages/ReactiveGenerator)
[![NuGet](https://img.shields.io/nuget/dt/ReactiveGenerator.svg)](https://www.nuget.org/packages/ReactiveGenerator)

A C# source generator that automatically implements property change notifications using either standard `INotifyPropertyChanged` or ReactiveUI patterns. It provides efficient code generation for properties with full type safety and design-time support.

## Features

### Core Features
- Automatic `INotifyPropertyChanged` implementation
- Class-level and property-level reactive declarations
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

### Class-Level Reactive Declaration

```csharp
[Reactive]
public partial class Person
{
    public partial string FirstName { get; set; }
    public partial string LastName { get; set; }
}
```

### Property-Level Reactive Declaration

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

## Generation Rules

### Attribute Rules
1. `[Reactive]` can be applied at:
    - Class level: All partial properties in the class become reactive
    - Property level: Individual properties become reactive
2. Properties marked with `[Reactive]` must be declared as `partial`
3. Classes containing reactive properties must be declared as `partial`

### INPC Implementation Rules
1. If a class is not inheriting from `ReactiveObject`:
    - INPC interface is automatically implemented
    - PropertyChanged event is generated
    - OnPropertyChanged methods are generated
2. INPC implementation is generated only once in the inheritance chain
3. Base class INPC implementation is respected and reused

### Property Generation Rules
1. Access Modifiers:
    - Property access level is preserved
    - Get/set accessor modifiers are preserved
    - Generated backing fields follow property accessibility

2. Nullability:
    - Nullable annotations are preserved
    - Reference type nullability is correctly propagated
    - Value type nullability is handled appropriately

3. Inheritance:
    - Virtual/override modifiers are preserved
    - Base class properties are respected
    - Multiple inheritance levels are handled correctly

4. Field Generation:
    - Modern mode (default): Uses C# field keyword
    - Legacy mode: Uses explicit backing fields with underscore prefix
    - Static PropertyChangedEventArgs instances are cached per property

### Observable Generation Rules
1. For each reactive property:
    - Type-safe WhenAny extension method is generated
    - Weak event handling is implemented
    - Thread-safe subscription management is provided

2. Observable Lifecycle:
    - Automatic cleanup of unused subscriptions
    - Memory leak prevention through weak references
    - Proper disposal handling

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

### Common Issues and Solutions

#### Missing Partial Declarations
```csharp
// ❌ Wrong
[Reactive]
public class Example
{
    public string Property { get; set; }
}

// ✅ Correct
[Reactive]
public partial class Example
{
    public partial string Property { get; set; }
}
```

#### ReactiveUI Base Class
```csharp
// ❌ Wrong: Missing ReactiveObject base
[Reactive]
public partial class Example
{
    public partial string Property { get; set; }
}

// ✅ Correct
[Reactive]
public partial class Example : ReactiveObject
{
    public partial string Property { get; set; }
}
```

## License

ReactiveGenerator is licensed under the MIT license.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request. For major changes, please open an issue first to discuss what you would like to change.
