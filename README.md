# ReactiveGenerator

[![CI](https://github.com/wieslawsoltes/ReactiveGenerator/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/ReactiveGenerator/actions/workflows/build.yml)
[![NuGet](https://img.shields.io/nuget/v/ReactiveGenerator.svg)](https://www.nuget.org/packages/ReactiveGenerator)
[![NuGet](https://img.shields.io/nuget/dt/ReactiveGenerator.svg)](https://www.nuget.org/packages/ReactiveGenerator)

A C# source generator that automatically implements property change notifications using either standard `INotifyPropertyChanged` or ReactiveUI patterns. It generates efficient and clean code for your properties while maintaining full type safety and design-time support.

## Features

- Automatic `INotifyPropertyChanged` implementation
- Support for ReactiveUI's `ReactiveObject` pattern
- Inheritance-aware property generation
- Support for all property access modifiers
- Cached `PropertyChangedEventArgs` for better performance
- Clean and efficient generated code

## Installation

Install the NuGet package:

```bash
dotnet add package ReactiveGenerator
```

## Usage

### Standard INPC Implementation

For classes that need standard `INotifyPropertyChanged` implementation, simply mark your properties with the `[Reactive]` attribute:

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

The generator will:
- Implement `INotifyPropertyChanged`
- Generate backing fields
- Add property change notifications
- Cache `PropertyChangedEventArgs` instances
- Handle inheritance correctly

#### Generated Code

```csharp
public partial class Person : INotifyPropertyChanged
{
    private static readonly PropertyChangedEventArgs _firstNameChangedEventArgs = new PropertyChangedEventArgs(nameof(FirstName));
    private static readonly PropertyChangedEventArgs _lastNameChangedEventArgs = new PropertyChangedEventArgs(nameof(LastName));
    private static readonly PropertyChangedEventArgs _ageChangedEventArgs = new PropertyChangedEventArgs(nameof(Age));

    public event PropertyChangedEventHandler PropertyChanged;

    private string _firstName;
    private string _lastName;
    private int _age;

    public partial string FirstName 
    {
        get => _firstName;
        set 
        {
            if (!Equals(_firstName, value))
            {
                _firstName = value;
                PropertyChanged?.Invoke(this, _firstNameChangedEventArgs);
            }
        }
    }
    // LastName and Age properties similarly implemented
}
```

### ReactiveUI Integration

When using ReactiveUI, inherit from `ReactiveObject` and mark properties with `[Reactive]`:

```csharp
public partial class Vehicle : ReactiveObject
{
    [Reactive]
    public partial string Make { get; set; }

    [Reactive]
    public partial string Model { get; set; }

    [Reactive]
    public partial int Year { get; set; }
}
```

The generator will use ReactiveUI's built-in `RaiseAndSetIfChanged` method:

#### Generated Code

```csharp
public partial class Vehicle
{
    private string _make;
    private string _model;
    private int _year;

    public partial string Make
    {
        get => _make;
        set => this.RaiseAndSetIfChanged(ref _make, value);
    }
    // Model and Year properties similarly implemented
}
```

### Inheritance Support

The generator properly handles inheritance scenarios:

```csharp
public partial class Person
{
    [Reactive]
    public partial string FirstName { get; set; }
}

public partial class Student : Person
{
    [Reactive]
    public partial string StudentId { get; set; }
}

public partial class Teacher : Person
{
    [Reactive]
    public partial List<Student> Students { get; set; }
}
```

### Access Modifiers

The generator respects all access modifiers:

```csharp
public partial class Example
{
    [Reactive]
    public partial string PublicProperty { get; set; }

    [Reactive]
    protected partial string ProtectedProperty { get; private set; }

    [Reactive]
    internal partial string InternalProperty { get; protected set; }

    [Reactive]
    private partial string PrivateProperty { get; set; }
}
```

### Read-only Properties

You can create read-only properties by omitting the setter:

```csharp
public partial class Document
{
    [Reactive]
    public partial string Id { get; }
}
```

## Notes

- Properties must be marked as `partial`
- Classes containing reactive properties must be marked as `partial`
- The `[Reactive]` attribute must be applied to each property that needs change notifications
- For ReactiveUI integration, classes must inherit from `ReactiveObject`
- The generator automatically determines whether to use INPC or ReactiveUI patterns based on the class hierarchy

## License

ReactiveGenerator is licensed under the [MIT license](LICENSE.TXT).
