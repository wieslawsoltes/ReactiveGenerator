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
- Property-level opt-out using `[IgnoreReactive]`
- Support for custom property implementations
- Automatic `WhenAnyValue` observable generation for reactive properties
- Support for modern C# field keyword and legacy backing fields
- Full nullable reference type support
- Inheritance-aware property generation
- Flexible property access modifiers
- Cached `PropertyChangedEventArgs` for performance optimization

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

### Advanced Property Control

#### Opting Out of Class-Level Reactivity

When a class is marked with `[Reactive]`, you can selectively opt out specific properties using `[IgnoreReactive]`:

```csharp
[Reactive]
public partial class Shape
{
    public partial string? Name { get; set; }         // Will be reactive

    [IgnoreReactive]
    public partial string? Tag { get; set; }          // Won't be reactive
}
```

#### Custom Property Implementations

You can provide custom implementations for properties marked with `[IgnoreReactive]`:

```csharp
[Reactive]
public partial class Shape
{
    public partial string? Name { get; set; }         // Generated reactive implementation

    [IgnoreReactive]
    public partial string? Tag { get; set; }          // Custom implementation below
}

public partial class Shape
{
    private string? _tag;

    public partial string? Tag
    {
        get => _tag; 
        set => _tag = value;
    }
}
```

This allows you to:
- Mix generated and custom property implementations
- Take full control of specific properties when needed
- Maintain the reactive pattern for most properties while customizing others

### ReactiveUI Integration

```csharp
public partial class ViewModel : ReactiveObject
{
    [Reactive]
    public partial string SearchText { get; set; }

    [Reactive]
    public partial string Status { get; set; }

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
4. `[IgnoreReactive]` can be used to:
    - Opt out of class-level reactivity
    - Allow custom property implementations

### Property Implementation Rules
1. Default Generation:
    - Properties without implementations get reactive implementations
    - Properties with existing implementations are respected
2. Mixed Implementations:
    - Can mix generated and custom implementations in the same class
    - Custom implementations take precedence over generation
3. Implementation Override:
    - Mark property with `[IgnoreReactive]` to provide custom implementation
    - Custom implementation can be in any partial class declaration

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
