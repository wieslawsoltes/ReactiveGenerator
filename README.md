# ReactiveGenerator

[![CI](https://github.com/wieslawsoltes/ReactiveGenerator/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/ReactiveGenerator/actions/workflows/build.yml)

[![NuGet](https://img.shields.io/nuget/v/ReactiveGenerators.vg)](https://www.nuget.org/packages/ReactiveGenerator)
[![NuGet](https://img.shields.io/nuget/dt/ReactiveGenerator.svg)](https://www.nuget.org/packages/ReactiveGenerator)

Source generator for implementing INotifyPropertyChanged using C# partial properties

## Usage

### Without MVVM library

The ReactiveGenerator will add INotifyPropertyChanged, backing fields and generate property changed event invocation inside setter.

```C#
public partial class Person
{
    [Reactive]
    public partial string FirstName { get; set; }

    [Reactive]
    public partial string LastName { get; set; }

    [Reactive]
    public partial int Age { get; set; }
}

public partial class Student : Person
{
    [Reactive]
    public partial string Address { get; set; }
}

public partial class Teacher : Person
{
    [Reactive]
    public partial List<Student> Students { get; set; }
}

```

### Wtih ReactiveUI

The ReactiveGenerator will add backing fields and generate property changed event invocation inside setter using ReactiveUI buil-in extension method.

```
public partial class Car : ReactiveObject
{
    [Reactive]
    public partial string Make { get; set; }
}
```

## License

ReactiveGenerator is licensed under the [MIT license](LICENSE.TXT).
