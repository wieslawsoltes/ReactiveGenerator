# ReactiveGenerator

[![CI](https://github.com/wieslawsoltes/ReactiveGenerator/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/ReactiveGenerator/actions/workflows/build.yml)
[![NuGet](https://img.shields.io/nuget/v/ReactiveGenerator.svg)](https://www.nuget.org/packages/ReactiveGenerator)
[![NuGet](https://img.shields.io/nuget/dt/ReactiveGenerator.svg)](https://www.nuget.org/packages/ReactiveGenerator)

ReactiveGenerator is a modern C# source generator that automates property change notification implementation, supporting both standard `INotifyPropertyChanged` and ReactiveUI patterns. It provides a robust, type-safe solution for generating reactive properties with comprehensive design-time support and code analysis capabilities.

## Reducing Boilerplate

### Before ReactiveGenerator

```csharp
public class UserViewModel : ReactiveObject
{
    private string _firstName;
    public string FirstName
    {
        get => _firstName;
        set => this.RaiseAndSetIfChanged(ref _firstName, value);
    }

    private string _lastName;
    public string LastName
    {
        get => _lastName;
        set => this.RaiseAndSetIfChanged(ref _lastName, value);
    }

    private readonly ObservableAsPropertyHelper<string> _fullName;
    public string FullName => _fullName.Value;

    private readonly ObservableAsPropertyHelper<bool> _hasName;
    public bool HasName => _hasName.Value;

    public UserViewModel()
    {
        _fullName = this.WhenAnyValue(x => x.FirstName, x => x.LastName)
            .Select(tuple => $"{tuple.Item1} {tuple.Item2}".Trim())
            .ToProperty(this, x => x.FullName);

        _hasName = this.WhenAnyValue(x => x.FullName)
            .Select(name => !string.IsNullOrEmpty(name))
            .ToProperty(this, x => x.HasName);
    }
}
```

### After ReactiveGenerator

```csharp
[Reactive]
public partial class UserViewModel : ReactiveObject
{
    public partial string FirstName { get; set; }
    public partial string LastName { get; set; }

    [ObservableAsProperty]
    public partial string FullName { get; }

    [ObservableAsProperty]
    public partial bool HasName { get; }

    public UserViewModel()
    {
        // Generated extension methods make the code more readable
        this.WhenAnyFirstName()
            .CombineLatest(this.WhenAnyLastName())
            .Select(names => $"{names.First} {names.Second}".Trim())
            .ToProperty(this, x => x.FullName);

        this.WhenAnyFullName()
            .Select(name => !string.IsNullOrEmpty(name))
            .ToProperty(this, x => x.HasName);
    }
}
```

## Quick Start Samples

### Basic MVVM with Reactive Properties

```csharp
[Reactive]
public partial class UserViewModel : ReactiveObject
{
    // Simple properties - automatically implements INotifyPropertyChanged
    public partial string FirstName { get; set; }
    public partial string LastName { get; set; }
    
    // Computed property with ObservableAsPropertyHelper
    [ObservableAsProperty]
    public partial string FullName { get; }
    
    public UserViewModel()
    {
        // Use generated WhenAnyValue extension method
        this.WhenAnyValue(x => x.FirstName, x => x.LastName)
            .Select(tuple => $"{tuple.Item1} {tuple.Item2}".Trim())
            .ToProperty(this, x => x.FullName);
    }
}
```

### Search Form Example

```csharp
public partial class SearchViewModel : ReactiveObject
{
    // Reactive properties for user input
    [Reactive]
    public partial string SearchTerm { get; set; }

    [Reactive]
    public partial bool IsSearching { get; set; }

    // Results as ObservableAsProperty
    [ObservableAsProperty]
    public partial IReadOnlyList<SearchResult> Results { get; }

    public SearchViewModel()
    {
        // Use the generated extension method
        this.WhenAnySearchTerm()
            .Throttle(TimeSpan.FromMilliseconds(400))
            .Do(_ => IsSearching = true)
            .Select(term => PerformSearch(term))
            .Do(_ => IsSearching = false)
            .ToProperty(this, x => x.Results);
    }
}
```

### Form Validation Example

```csharp
public partial class RegistrationForm : ReactiveObject
{
    [Reactive]
    public partial string Email { get; set; }

    [Reactive]
    public partial string Password { get; set; }
    
    [ObservableAsProperty]
    public partial bool IsValid { get; }
    
    [ObservableAsProperty]
    public partial string ValidationMessage { get; }

    public RegistrationForm()
    {
        // Combine multiple properties for validation
        this.WhenAnyValue(x => x.Email, x => x.Password)
            .Select(tuple => ValidateForm(tuple.Item1, tuple.Item2))
            .ToProperty(this, x => x.ValidationMessage);

        this.WhenAnyValue(x => x.ValidationMessage)
            .Select(msg => string.IsNullOrEmpty(msg))
            .ToProperty(this, x => x.IsValid);
    }
}
```

### Collections and Lists

```csharp
public partial class TodoListViewModel : ReactiveObject
{
    [Reactive]
    public partial ObservableCollection<TodoItem> Items { get; set; }

    [Reactive]
    public partial string NewItemText { get; set; }

    [ObservableAsProperty]
    public partial int CompletedCount { get; }

    [ObservableAsProperty]
    public partial int TotalCount { get; }

    public TodoListViewModel()
    {
        Items = new ObservableCollection<TodoItem>();

        // Track collection changes
        this.WhenAnyValue(x => x.Items)
            .Select(items => items.Count)
            .ToProperty(this, x => x.TotalCount);

        this.WhenAnyValue(x => x.Items)
            .Select(items => items.Count(i => i.IsCompleted))
            .ToProperty(this, x => x.CompletedCount);
    }
}

[Reactive]
public partial class TodoItem
{
    public partial string Text { get; set; }
    public partial bool IsCompleted { get; set; }
}
```

## Key Features

### Property Change Notifications
- Automated `INotifyPropertyChanged` implementation
- Support for ReactiveUI's `ReactiveObject` pattern
- Efficient, cached `PropertyChangedEventArgs`
- Thread-safe weak event handling
- Smart code analysis with refactoring suggestions

### ObservableAsPropertyHelper Generation
- Automated generation of `ObservableAsPropertyHelper` properties
- Simplifies ReactiveUI's observable-to-property pattern
- Reduces boilerplate code for computed properties
- Enhances readability and maintainability
- Thread-safe implementation with weak event handling

### Code Analysis & Fixes
- Intelligent detection of convertible properties
- Code fix providers for automatic conversion to reactive properties
- Bulk refactoring support (file, project, and solution-wide)
- Design-time validation and suggestions

### Flexible Declaration Options
- Class-level reactivity with `[Reactive]` attribute
- Property-level granular control with `[Reactive]` and `[ObservableAsProperty]`
- Selective opt-out using `[IgnoreReactive]`
- Support for custom property implementations
- Inheritance-aware property generation

## Installation & Setup

### NuGet Package
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

## Advanced Usage

### Selective Property Control
```csharp
[Reactive]
public partial class Shape
{
    public partial string Name { get; set; }         // Reactive

    [IgnoreReactive]
    public partial string Tag { get; set; }          // Non-reactive

    // Custom implementation with backing field
    private Color _color = Colors.White;
    [IgnoreReactive]
    public partial Color Color 
    {
        get => _color;
        set
        {
            if (_color != value)
            {
                _color = value;
                OnPropertyChanged();
            }
        }
    }
}
```

### Inheritance Example

```csharp
[Reactive]
public partial class Animal
{
    public partial string Name { get; set; }
}

[Reactive]
public partial class Dog : Animal
{
    public partial string Breed { get; set; }
    
    [ObservableAsProperty]
    public partial string DisplayName { get; }
    
    public Dog()
    {
        this.WhenAnyValue(x => x.Name, x => x.Breed)
            .Select(t => $"{t.Item1} ({t.Item2})")
            .ToProperty(this, x => x.DisplayName);
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

## Troubleshooting

### Common Issues

#### Missing Partial Declarations
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

#### Incorrect ObservableAsProperty Usage
```csharp
// ❌ Incorrect
public class ViewModel : ReactiveObject
{
    [ObservableAsProperty]
    public string ComputedValue { get; }
}

// ✅ Correct
public partial class ViewModel : ReactiveObject
{
    [ObservableAsProperty]
    public partial string ComputedValue { get; }
}
```

### Implementation Details

#### Event Management
- Uses `WeakEventManager<TDelegate>` for efficient memory management
- Thread-safe event handling with concurrent collections
- Automatic cleanup of unused subscriptions
- Proper handling of generated `PropertyChangedEventArgs` instances

#### Type Resolution
- Cross-assembly type inheritance support
- Full generic type constraint validation
- Proper handling of nullable reference types
- Support for nested type hierarchies

#### Code Generation
- Deterministic output for reliable builds
- Support for source link and debugging
- Efficient handling of large type hierarchies
- Proper XML documentation generation

### Known Limitations
- Properties must be declared as `partial`
- Classes must be declared as `partial`
- Custom implementations require `[IgnoreReactive]` attribute
- Base class implementations must be compatible with `INotifyPropertyChanged`
- Generic type constraints must be valid at declaration site

## Contributing

1. Fork the repository
2. Create a feature branch
3. Submit a Pull Request

For major changes, please open an issue first to discuss the proposed changes.

## License

ReactiveGenerator is licensed under the MIT license. See [LICENSE](LICENSE.TXT) file for details.

## Contact

For questions, feedback, or issues, please open a GitHub issue.
