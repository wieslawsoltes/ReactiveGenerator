# ReactiveGenerator

[![CI](https://github.com/wieslawsoltes/ReactiveGenerator/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/ReactiveGenerator/actions/workflows/build.yml)
[![NuGet](https://img.shields.io/nuget/v/ReactiveGenerator.svg)](https://www.nuget.org/packages/ReactiveGenerator)
[![NuGet](https://img.shields.io/nuget/dt/ReactiveGenerator.svg)](https://www.nuget.org/packages/ReactiveGenerator)

A C# source generator that automatically implements property change notifications using either standard `INotifyPropertyChanged` or ReactiveUI patterns. It generates efficient and clean code for your properties while maintaining full type safety and design-time support. Requires C# 13 and .NET 9 or later.

## Features

### Core Features
- Automatic `INotifyPropertyChanged` implementation
- Support for ReactiveUI's `ReactiveObject` pattern
- Modern C# 13 field keyword support for cleaner property implementation
- Legacy mode with explicit backing fields (also C# 13)
- Full nullable reference type support
- Inheritance-aware property generation
- Support for all property access modifiers
- Cached `PropertyChangedEventArgs` for better performance

### Performance Optimizations
- Static caching of PropertyChangedEventArgs instances
- Efficient property change detection using Equals
- Minimal memory allocation during property updates
- Zero-overhead property access for unchanged values

### Developer Experience
- Clean and readable generated code
- Full IntelliSense support
- Design-time error detection
- Comprehensive nullability annotations
- Flexible configuration options
- Seamless integration with existing codebases

## Installation

### NuGet Package

Install using the .NET CLI:
```bash
dotnet add package ReactiveGenerator
```

Or using the Package Manager Console:
```powershell
Install-Package ReactiveGenerator
```

Or by adding directly to your .csproj:
```xml
<ItemGroup>
    <PackageReference Include="ReactiveGenerator" Version="x.y.z" />
</ItemGroup>
```

### Prerequisites
- .NET 9.0 or later
- C# 13.0 or later (required for both modern and legacy modes)
- `<LangVersion>preview</LangVersion>` in project file
- Visual Studio 2022 or compatible IDE

## Configuration

### Property Implementation Modes

The generator supports two distinct modes for implementing property backing storage:

#### 1. Modern Mode (Default)
Uses C# 13's field keyword for cleaner implementation. This mode:
- Reduces boilerplate code
- Improves code readability
- Maintains encapsulation
- Requires C# 12 or later

Example of generated code:
```csharp
public partial string Property
{
    get => field;
    set => SetField(ref field, value);
}
```

#### 2. Legacy Mode
Uses traditional explicit backing fields. This mode:
- Supports older C# versions
- Provides more explicit code
- Offers better compatibility with older tooling
- Allows custom backing field naming conventions

Example of generated code:
```csharp
private string _property;
public partial string Property
{
    get => _property;
    set => SetField(ref _property, value);
}
```

### Configuration Options

#### MSBuild Properties

Add these to your project file (.csproj) to customize the generator's behavior:

```xml
<PropertyGroup>
    <!-- Required for C# 13 field keyword support -->
    <LangVersion>preview</LangVersion>
    
    <!-- Optional: Enable legacy mode with explicit backing fields -->
    <UseBackingFields>true</UseBackingFields>
</PropertyGroup>
```

Note: When using the generator with the field keyword (modern mode), you must set `<LangVersion>preview</LangVersion>` in your project file as the feature is part of C# 13 preview.

## Usage

### Basic INPC Implementation

#### Simple Property Declaration

```csharp
public partial class Person
{
    [Reactive]
    public partial string FirstName { get; set; }

    [Reactive]
    public partial string LastName { get; set; }

    [Reactive]
    public partial int Age { get; set; }
}
```

#### Generated Implementation (Modern Mode)

```csharp
public partial class Person : INotifyPropertyChanged
{
    // Cached event args for performance
    private static readonly PropertyChangedEventArgs _firstNameChangedEventArgs = new PropertyChangedEventArgs(nameof(FirstName));
    private static readonly PropertyChangedEventArgs _lastNameChangedEventArgs = new PropertyChangedEventArgs(nameof(LastName));
    private static readonly PropertyChangedEventArgs _ageChangedEventArgs = new PropertyChangedEventArgs(nameof(Age));

    // INotifyPropertyChanged implementation
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected virtual void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        PropertyChanged?.Invoke(this, args);
    }

    // Generated properties
    public partial string FirstName 
    {
        get => field;
        set 
        {
            if (!Equals(field, value))
            {
                field = value;
                OnPropertyChanged(_firstNameChangedEventArgs);
            }
        }
    }

    // Additional properties similarly implemented
}
```

### ReactiveUI Integration

#### Basic ReactiveUI Usage

```csharp
public partial class Vehicle : ReactiveObject
{
    [Reactive]
    public partial string? Make { get; set; }  // Nullable reference type

    [Reactive]
    public partial string Model { get; set; }  // Non-nullable reference type

    [Reactive]
    public partial int Year { get; set; }      // Value type
}
```

#### Advanced ReactiveUI Patterns

```csharp
public partial class AdvancedViewModel : ReactiveObject
{
    [Reactive]
    public partial ObservableCollection<string> Items { get; set; }

    [Reactive]
    public partial bool IsLoading { get; set; }

    // Read-only property
    [Reactive]
    public partial string Status { get; }

    // Property with custom setter visibility
    [Reactive]
    public partial int Count { get; private set; }
}
```

### Inheritance and Interface Implementation

#### Base Classes

```csharp
public partial class EntityBase
{
    [Reactive]
    public partial Guid Id { get; set; }

    [Reactive]
    public partial DateTime CreatedAt { get; set; }
}

public partial class User : EntityBase
{
    [Reactive]
    public partial string Username { get; set; }

    [Reactive]
    public partial string Email { get; set; }
}
```

#### Interface Implementation

```csharp
public interface INamedEntity
{
    string Name { get; set; }
}

public partial class NamedEntity : INamedEntity
{
    [Reactive]
    public partial string Name { get; set; }
}
```

### Access Modifiers and Visibility

```csharp
public partial class Example
{
    // Public property with public getter/setter
    [Reactive]
    public partial string PublicProperty { get; set; }

    // Protected property with private setter
    [Reactive]
    protected partial string ProtectedProperty { get; private set; }

    // Internal property with protected setter
    [Reactive]
    internal partial string InternalProperty { get; protected set; }

    // Private property
    [Reactive]
    private partial string PrivateProperty { get; set; }

    // Protected internal property
    [Reactive]
    protected internal partial string ProtectedInternalProperty { get; set; }

    // Private protected property
    [Reactive]
    private protected partial string PrivateProtectedProperty { get; set; }
}
```

### Nullable Reference Types

```csharp
public partial class NullableExample
{
    // Nullable string
    [Reactive]
    public partial string? NullableString { get; set; }

    // Non-nullable string
    [Reactive]
    public partial string RequiredString { get; set; }

    // Nullable complex type
    [Reactive]
    public partial List<string>? OptionalList { get; set; }

    // Non-nullable complex type
    [Reactive]
    public partial List<string> RequiredList { get; set; }
}
```

## Best Practices

### Performance Considerations

1. **Property Change Detection**
    - Use value equality comparison for reference types
    - Implement IEquatable<T> for complex types
    - Consider custom equality comparers for specific scenarios

2. **Event Handler Management**
    - Weak event patterns for long-lived objects
    - Proper event handler cleanup in disposable objects
    - Avoid circular references in event handlers

### Design Patterns

1. **MVVM Pattern Integration**
   ```csharp
   public partial class ViewModel : ReactiveObject
   {
       [Reactive]
       public partial Model DataModel { get; set; }

       [Reactive]
       public partial bool IsBusy { get; set; }

       // Command properties don't need [Reactive]
       public ReactiveCommand<Unit, Unit> SaveCommand { get; }
   }
   ```

2. **Repository Pattern**
   ```csharp
   public partial class Repository<T> where T : class
   {
       [Reactive]
       public partial IReadOnlyList<T> Items { get; private set; }

       [Reactive]
       public partial bool IsLoading { get; private set; }
   }
   ```

## Common Pitfalls and Solutions

### 1. Partial Declaration Missing
```csharp
// ❌ Wrong
public class Example
{
    [Reactive]
    public partial string Property { get; set; }
}

// ✅ Correct
public partial class Example
{
    [Reactive]
    public partial string Property { get; set; }
}
```

### 2. Reactive Attribute on Non-Partial Property
```csharp
// ❌ Wrong
public partial class Example
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

### 3. Missing ReactiveObject Base Class
```csharp
// ❌ Wrong: Using ReactiveUI features without inheritance
public partial class Example
{
    [Reactive]
    public partial string Property { get; set; }
}

// ✅ Correct: Inheriting from ReactiveObject
public partial class Example : ReactiveObject
{
    [Reactive]
    public partial string Property { get; set; }
}
```

## Advanced Scenarios

### Custom Property Change Notifications

```csharp
public partial class AdvancedExample : ReactiveObject
{
    [Reactive]
    public partial decimal Price { get; set; }

    [Reactive]
    public partial int Quantity { get; set; }

    // Dependent property
    public decimal Total => Price * Quantity;

    // Constructor sets up property change propagation
    public AdvancedExample()
    {
        this.WhenAnyValue(x => x.Price, x => x.Quantity)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(Total)));
    }
}
```

### Integration with Other Frameworks

#### Entity Framework Core
```csharp
public partial class DbEntity
{
    [Reactive]
    public partial int Id { get; set; }

    [Reactive]
    public partial string Name { get; set; }

    // EF Core navigation property
    public virtual ICollection<RelatedEntity> RelatedEntities { get; set; }
}
```

#### ASP.NET Core MVC
```csharp
public partial class ViewModel
{
    [Reactive]
    [Required]
    [StringLength(100)]
    public partial string Name { get; set; }

    [Reactive]
    [EmailAddress]
    public partial string Email { get; set; }
}
```

## Notes

- Properties must be marked as `partial`
- Classes containing reactive properties must be marked as `partial`
- The `[Reactive]` attribute must be applied to each property that needs change notifications
- For ReactiveUI integration, classes must inherit from `ReactiveObject`
- The generator automatically determines whether to use INPC or ReactiveUI patterns based on the class hierarchy
- Nullable reference types are fully supported and respected in the generated code
- Compiler errors will help identify common configuration mistakes
- Property change notifications are thread-safe when using ReactiveUI

## License

ReactiveGenerator is licensed under the [MIT license](LICENSE).

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request. For major changes, please open an issue first to discuss what you would like to change.
