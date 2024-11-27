# ReactiveGenerator

[![CI](https://github.com/wieslawsoltes/ReactiveGenerator/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/ReactiveGenerator/actions/workflows/build.yml)
[![NuGet](https://img.shields.io/nuget/v/ReactiveGenerator.svg)](https://www.nuget.org/packages/ReactiveGenerator)
[![NuGet](https://img.shields.io/nuget/dt/ReactiveGenerator.svg)](https://www.nuget.org/packages/ReactiveGenerator)

ReactiveGenerator is a modern C# source generator that automates property change notification implementation, supporting both standard `INotifyPropertyChanged` and ReactiveUI patterns. It provides a robust, type-safe solution for generating reactive properties with comprehensive design-time support.

## Key Features

### Property Change Notifications
- Automated `INotifyPropertyChanged` implementation
- Support for ReactiveUI's `ReactiveObject` pattern
- Efficient, cached `PropertyChangedEventArgs`
- Thread-safe weak event handling

### Flexible Declaration Options
- Class-level reactivity with `[Reactive]` attribute
- Property-level granular control
- Selective opt-out using `[IgnoreReactive]`
- Support for custom property implementations

### Developer Experience
- Full nullable reference type support
- Inheritance-aware property generation
- Configurable backing field generation
- Comprehensive design-time support
- Automatic `WhenAnyValue` observable generation

## Quick Start

### Installation

```bash
dotnet add package ReactiveGenerator
```

### Prerequisites

- .NET 9.0+
- C# 13.0+
- Visual Studio 2022 or compatible IDE
- Project configuration:
  ```xml
  <LangVersion>preview</LangVersion>
  ```

### Basic Usage

Class-level reactive properties:
```csharp
[Reactive]
public partial class Person
{
    public partial string FirstName { get; set; }
    public partial string LastName { get; set; }
}
```

Property-level reactive declarations:
```csharp
public partial class Person
{
    [Reactive]
    public partial string FirstName { get; set; }

    [Reactive]
    public partial string LastName { get; set; }
}
```

## Advanced Usage

### Selective Property Control

Opt out of class-level reactivity:
```csharp
[Reactive]
public partial class Shape
{
    public partial string Name { get; set; }         // Reactive

    [IgnoreReactive]
    public partial string Tag { get; set; }          // Non-reactive
}
```

### ReactiveUI Integration

```csharp
public partial class ViewModel : ReactiveObject
{
    [Reactive]
    public partial string SearchText { get; set; }

    public ViewModel()
    {
        this.WhenAnySearchText()
            .Subscribe(text => ProcessSearch(text));
    }
}
```

### Custom Implementations

```csharp
[Reactive]
public partial class Component
{
    [IgnoreReactive]
    public partial string Id { get; set; }
}

public partial class Component
{
    private string _id = Guid.NewGuid().ToString();

    public partial string Id
    {
        get => _id;
        private set => _id = value;
    }
}
```

## Configuration

### MSBuild Properties

```xml
<PropertyGroup>
    <!-- Use explicit backing fields instead of field keyword -->
    <UseBackingFields>true</UseBackingFields>
</PropertyGroup>
```

## Architecture

### Generated Components

ReactiveGenerator produces several key types:

- `PropertyObserver<TSource, TProperty>`: Handles property observation
- `WeakEventManager<TDelegate>`: Manages thread-safe weak events
- Property-specific extension methods (e.g., `WhenAnyPropertyName`)

### Implementation Rules

1. Property Generation:
    - Properties without implementations receive reactive implementations
    - Existing implementations are preserved
    - Custom implementations take precedence

2. Declaration Requirements:
    - Classes containing reactive properties must be `partial`
    - Reactive properties must be declared as `partial`
    - `[Reactive]` can be applied at class or property level

## Troubleshooting

### Common Issues

Missing partial declarations:
```csharp
// ❌ Incorrect
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

### Known Limitations

- Properties must be declared as partial
- Classes must be declared as partial
- Custom implementations require [IgnoreReactive] attribute

## Contributing

We welcome contributions! Please follow these steps:

1. Fork the repository
2. Create a feature branch
3. Submit a Pull Request
4. For major changes, open an issue first

## License

ReactiveGenerator is licensed under the MIT license. See [LICENSE](LICENSE) file for details.
