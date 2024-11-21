# ReactiveGenerator

[![CI](https://github.com/wieslawsoltes/ReactiveGenerator/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/ReactiveGenerator/actions/workflows/build.yml)
[![NuGet](https://img.shields.io/nuget/v/ReactiveGenerator.svg)](https://www.nuget.org/packages/ReactiveGenerator)
[![NuGet](https://img.shields.io/nuget/dt/ReactiveGenerator.svg)](https://www.nuget.org/packages/ReactiveGenerator)

A C# source generator that automatically implements property change notifications using either standard `INotifyPropertyChanged` or ReactiveUI patterns. It generates efficient and clean code for your properties while maintaining full type safety and design-time support. Requires C# 13 and .NET 9 or later.

## Features

### Core Features
- Automatic `INotifyPropertyChanged` implementation
- Support for ReactiveUI's `ReactiveObject` pattern
- Automatic `WhenAnyValue` observable generation for reactive properties
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
- Optimized WhenAnyValue observable implementation

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
- Requires C# 13 or later

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

### ReactiveUI Integration with WhenAnyValue Support

```csharp
public partial class UserViewModel : ReactiveObject
{
    [Reactive]
    public partial string Username { get; set; }

    [Reactive]
    public partial string Email { get; set; }

    [Reactive]
    public partial bool IsValid { get; private set; }

    public UserViewModel()
    {
        // Use generated type-safe WhenAnyValue extensions
        this.WhenAnyUsername()
            .Subscribe(username => Console.WriteLine($"Username changed to: {username}"));

        // Combine multiple property observations
        this.WhenAnyUsername()
            .CombineLatest(this.WhenAnyEmail())
            .Subscribe(tuple => 
            {
                var (username, email) = tuple;
                IsValid = !string.IsNullOrEmpty(username) && email.Contains("@");
            });
    }
}
```

### Advanced Scenarios

#### Property Change Notifications with Validation

```csharp
public partial class AdvancedViewModel : ReactiveObject
{
    [Reactive]
    public partial decimal Price { get; set; }

    [Reactive]
    public partial int Quantity { get; set; }

    [Reactive]
    public partial string? ValidationMessage { get; private set; }

    public decimal Total => Price * Quantity;

    public AdvancedViewModel()
    {
        // Validate price changes
        this.WhenAnyPrice()
            .Subscribe(price => 
            {
                ValidationMessage = price < 0 
                    ? "Price cannot be negative" 
                    : null;
            });

        // Update total on any relevant change
        this.WhenAnyPrice()
            .CombineLatest(this.WhenAnyQuantity())
            .Select(tuple => tuple.Item1 * tuple.Item2)
            .Subscribe(total => Console.WriteLine($"Total: {total:C}"));
    }
}
```

#### Working with Collections

```csharp
public partial class CollectionViewModel : ReactiveObject
{
    [Reactive]
    public partial ObservableCollection<string> Items { get; set; } = new();

    [Reactive]
    public partial int SelectedIndex { get; set; }

    public CollectionViewModel()
    {
        // Monitor collection changes
        this.WhenAnyItems()
            .SelectMany(items => items.ToObservable())
            .Subscribe(_ => UpdateUI());

        // Monitor selection changes
        this.WhenAnySelectedIndex()
            .Where(index => index >= 0 && index < Items.Count)
            .Subscribe(index => Console.WriteLine($"Selected: {Items[index]}"));
    }
}
```

### Best Practices

#### 1. Managing Subscriptions

```csharp
public partial class ViewModel : ReactiveObject, IDisposable
{
    private readonly CompositeDisposable _disposables = new();

    [Reactive]
    public partial string SearchText { get; set; }

    public ViewModel()
    {
        this.WhenAnySearchText()
            .Throttle(TimeSpan.FromMilliseconds(300))
            .Subscribe(text => PerformSearch(text))
            .DisposeWith(_disposables);
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
```

#### 2. Property Change Detection

```csharp
public partial class OrderViewModel : ReactiveObject
{
    [Reactive]
    public partial decimal Price { get; set; }

    // Implement IEquatable<T> for complex types
    [Reactive]
    public partial CustomType ComplexProperty { get; set; }
}
```

#### 3. Thread Safety

```csharp
public partial class ThreadSafeViewModel : ReactiveObject
{
    [Reactive]
    public partial string Status { get; set; }

    public ThreadSafeViewModel()
    {
        this.WhenAnyStatus()
            .ObserveOn(RxApp.MainThread)
            .Subscribe(status => UpdateUI(status));
    }
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

### 4. Improper WhenAnyValue Usage
```csharp
// ❌ Wrong: Not disposing subscriptions
public partial class Example : ReactiveObject
{
    public Example()
    {
        this.WhenAnyProperty()
            .Subscribe(value => DoSomething(value));
    }
}

// ✅ Correct: Proper subscription management
public partial class Example : ReactiveObject, IDisposable
{
    private readonly CompositeDisposable _cleanup = new();

    public Example()
    {
        this.WhenAnyProperty()
            .Subscribe(value => DoSomething(value))
            .DisposeWith(_cleanup);
    }

    public void Dispose() => _cleanup.Dispose();
}
```

## Advanced Features

### 1. Custom Property Change Notifications

```csharp
public partial class AdvancedExample : ReactiveObject
{
    [Reactive]
    public partial decimal Price { get; set; }

    [Reactive]
    public partial int Quantity { get; set; }

    // Dependent property
    public decimal Total => Price * Quantity;

    public AdvancedExample()
    {
        this.WhenAnyPrice()
            .CombineLatest(this.WhenAnyQuantity())
            .Subscribe(_ => this.RaisePropertyChanged(nameof(Total)));
    }
}
```

### 2. Integration with Entity Framework Core

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

### 3. ASP.NET Core MVC Integration

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

## Performance Considerations

1. **Property Change Detection**
    - Uses value equality comparison for reference types
    - Implements IEquatable<T> for complex types
    - Caches PropertyChangedEventArgs instances
    - Minimal allocations during property updates

2. **WhenAnyValue Optimization**
    - Efficient subscription management
    - Smart change detection to avoid unnecessary notifications
    - Thread-safe event handling
    - Memory-efficient implementation

## Notes

- Properties must be marked as `partial`
- Classes containing reactive properties must be marked as `partial`
- The `[Reactive]` attribute must be applied to each property that needs change notifications
- For ReactiveUI integration, classes must inherit from `ReactiveObject`
- The generator automatically determines whether to use INPC or ReactiveUI patterns based on the class hierarchy
- Nullable reference types are fully supported and respected in the generated code
- Compiler errors will help identify common configuration mistakes
- Property change notifications are thread-safe when using ReactiveUI
- WhenAnyValue extensions are automatically generated for all reactive properties

## License

ReactiveGenerator is licensed under the [MIT license](LICENSE).

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request. For major changes, please open an issue first to discuss what you would like to change.
