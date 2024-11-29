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

### ObservableAsPropertyHelper Generation

- Automated generation of `ObservableAsPropertyHelper` properties
- Simplifies ReactiveUI's observable-to-property pattern
- Reduces boilerplate code for computed properties
- Enhances readability and maintainability

### Flexible Declaration Options

- Class-level reactivity with `[Reactive]` attribute
- Property-level granular control with `[Reactive]` and `[ObservableAsProperty]`
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

#### Class-level Reactive Properties

```csharp
[Reactive]
public partial class Person
{
    public partial string FirstName { get; set; }
    public partial string LastName { get; set; }
}
```

#### Property-level Reactive Declarations

```csharp
public partial class Person
{
    [Reactive]
    public partial string FirstName { get; set; }

    [Reactive]
    public partial string LastName { get; set; }
}
```

#### ObservableAsPropertyHelper Properties

```csharp
public partial class DashboardViewModel : ReactiveObject
{
    public DashboardViewModel()
    {
        this.WhenAnyValue(x => x.DataStream)
            .Select(data => ProcessData(data))
            .ToProperty(this, x => x.ProcessedData);
    }

    [ObservableAsProperty]
    public partial string ProcessedData { get; }

    [Reactive]
    public partial IObservable<string> DataStream { get; set; }
}
```

## Advanced Usage

### ReactiveUI Integration

Seamlessly integrate with ReactiveUI to harness its powerful reactive programming features.

```csharp
public partial class ViewModel : ReactiveObject
{
    [Reactive]
    public partial string SearchText { get; set; }

    public ViewModel()
    {
        this.WhenAnyValue(x => x.SearchText)
            .Throttle(TimeSpan.FromMilliseconds(500))
            .Subscribe(text => ExecuteSearch(text));
    }

    private void ExecuteSearch(string query)
    {
        // Search logic here
    }
}
```

Using the generator's automatic `WhenAny` methods:

```csharp
public partial class ViewModel : ReactiveObject
{
    [Reactive]
    public partial string SearchText { get; set; }

    public ViewModel()
    {
        this.WhenAnySearchText()
            .Throttle(TimeSpan.FromMilliseconds(500))
            .Subscribe(text => ExecuteSearch(text));
    }
}
```

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

### ObservableAsPropertyHelper Integration

Simplify the creation of computed properties using `ObservableAsPropertyHelper`:

```csharp
public partial class StatisticsViewModel : ReactiveObject
{
    public StatisticsViewModel()
    {
        this.WhenAnyValue(x => x.Values)
            .Select(values => values.Average())
            .ToProperty(this, x => x.AverageValue);
    }

    [ObservableAsProperty]
    public partial double AverageValue { get; }

    [Reactive]
    public partial IList<double> Values { get; set; }
}
```

### Custom Implementations

Customize property implementations while still leveraging the generator:

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

### Advanced ObservableAsPropertyHelper Example

Leverage the power of ReactiveUI with less boilerplate:

```csharp
public partial class WeatherViewModel : ReactiveObject
{
    public WeatherViewModel()
    {
        this.WhenAnyValue(x => x.City)
            .Throttle(TimeSpan.FromMilliseconds(500))
            .SelectMany(city => FetchWeatherAsync(city))
            .ToProperty(this, x => x.CurrentWeather);
    }

    [ObservableAsProperty]
    public partial WeatherData CurrentWeather { get; }

    [Reactive]
    public partial string City { get; set; }

    private Task<WeatherData> FetchWeatherAsync(string city)
    {
        // Async API call to fetch weather data
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
- `ObservableAsPropertyHelper<T>` backing fields and properties

### Implementation Rules

1. **Property Generation:**
    - Properties without implementations receive reactive implementations
    - Existing implementations are preserved
    - Custom implementations take precedence

2. **Declaration Requirements:**
    - Classes containing reactive properties must be `partial`
    - Reactive properties must be declared as `partial`
    - `[Reactive]` and `[ObservableAsProperty]` can be applied at class or property level

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

#### Incorrect Usage of `[ObservableAsProperty]` Without `partial`

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

### Known Limitations

- Properties must be declared as `partial`
- Classes must be declared as `partial`
- Custom implementations require `[IgnoreReactive]` attribute

## Contributing

We welcome contributions! Please follow these steps:

1. Fork the repository
2. Create a feature branch
3. Submit a Pull Request
4. For major changes, open an issue first

## License

ReactiveGenerator is licensed under the MIT license. See [LICENSE](LICENSE.TXT) file for details.

## Contact

For questions or feedback, please open an issue or reach out to the maintainer.

---

By integrating `ObservableAsPropertyHelper` generation, ReactiveGenerator streamlines the development of reactive properties in your applications, allowing you to focus on core functionality with cleaner and more maintainable code.

# Additional Examples

## Full Example: Implementing a Live Search ViewModel

```csharp
using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ReactiveGenerator;
using ReactiveUI;

public partial class LiveSearchViewModel : ReactiveObject
{
    public LiveSearchViewModel()
    {
        this.WhenAnyValue(x => x.SearchQuery)
            .Throttle(TimeSpan.FromMilliseconds(300))
            .DistinctUntilChanged()
            .SelectMany(query => SearchAsync(query))
            .ToProperty(this, x => x.SearchResults);
    }

    [ObservableAsProperty]
    public partial IEnumerable<SearchResult> SearchResults { get; }

    [Reactive]
    public partial string SearchQuery { get; set; }

    private Task<IEnumerable<SearchResult>> SearchAsync(string query)
    {
        // Simulate an asynchronous search operation
        return Task.FromResult<IEnumerable<SearchResult>>(new List<SearchResult>());
    }
}
```

### Explanation

- **Reactive Property (`SearchQuery`):** Automatically implements `INotifyPropertyChanged` and notifies observers when the value changes.
- **ObservableAsProperty (`SearchResults`):** Automatically generates the backing field and property for `SearchResults`, reducing boilerplate code.
- **Reactive Logic:** Uses Reactive Extensions to throttle and process the search query, updating `SearchResults` automatically.

## Integrating with ReactiveUI Commands

```csharp
using System;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ReactiveGenerator;
using ReactiveUI;

public partial class LoginViewModel : ReactiveObject
{
    public LoginViewModel()
    {
        var canLogin = this.WhenAnyValue(
            x => x.Username,
            x => x.Password,
            (username, password) => 
                !string.IsNullOrWhiteSpace(username) && 
                !string.IsNullOrWhiteSpace(password));

        LoginCommand = ReactiveCommand.CreateFromTask(ExecuteLoginAsync, canLogin);

        LoginCommand.IsExecuting
            .ToProperty(this, x => x.IsLoggingIn);
    }

    [ObservableAsProperty]
    public partial bool IsLoggingIn { get; }

    [Reactive]
    public partial string Username { get; set; }

    [Reactive]
    public partial string Password { get; set; }

    public ReactiveCommand<Unit, bool> LoginCommand { get; }

    private Task<bool> ExecuteLoginAsync()
    {
        // Implement login logic
        return Task.FromResult(true);
    }
}
```

### Explanation

- **Reactive Properties (`Username`, `Password`):** Automatically notify when values change.
- **ObservableAsProperty (`IsLoggingIn`):** Reflects the execution state of `LoginCommand`.
- **Commands:** Integrates ReactiveUI commands with the generated properties.

## Professional Example: Data Synchronization ViewModel

```csharp
using System;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ReactiveGenerator;
using ReactiveUI;

public partial class DataSyncViewModel : ReactiveObject
{
    public DataSyncViewModel()
    {
        SyncCommand = ReactiveCommand.CreateFromTask(ExecuteSyncAsync);

        SyncCommand.IsExecuting
            .ToProperty(this, x => x.IsSyncing);

        this.WhenAnyValue(x => x.IsSyncing)
            .Subscribe(syncing =>
            {
                if (syncing)
                {
                    StatusMessage = "Synchronization in progress...";
                }
                else
                {
                    StatusMessage = "Synchronization complete.";
                }
            });
    }

    [ObservableAsProperty]
    public partial bool IsSyncing { get; }

    [Reactive]
    public partial string StatusMessage { get; private set; }

    public ReactiveCommand<Unit, Unit> SyncCommand { get; }

    private async Task ExecuteSyncAsync()
    {
        // Simulate data synchronization
        await Task.Delay(2000);
    }
}
```

### Explanation

- **ObservableAsProperty (`IsSyncing`):** Reflects the execution state of the `SyncCommand`.
- **Reactive Property (`StatusMessage`):** Updates based on the `IsSyncing` property.
- **Reactive Logic:** Automatically updates the UI based on the synchronization state.

---

By utilizing the `ObservableAsProperty` attribute and integrating with ReactiveUI, you can significantly reduce boilerplate code and create highly responsive, maintainable applications. The ReactiveGenerator enhances your development workflow, allowing you to focus on delivering value rather than writing repetitive code.

---

I hope this updated README addresses your concerns and restores the important parts related to ReactiveUI Integration. Let me know if there's anything else you'd like to add or modify!
