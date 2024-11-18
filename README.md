# ReactiveGenerator

[![NuGet](https://img.shields.io/nuget/v/ReactiveGeneratorsvg)](https://www.nuget.org/packages/ReactiveGenerator)
[![NuGet](https://img.shields.io/nuget/dt/ReactiveGenerator.svg)](https://www.nuget.org/packages/ReactiveGenerator)

Source generator for implementing INotifyPropertyChanged using C# partial properties

## Usage

Add `UseCanvas()` to tour app builder.

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

## License

ReactiveGenerator is licensed under the [MIT license](LICENSE.TXT).
