namespace ReactiveGeneratorDemo.ViewModels;

public partial class Person
{
    [Reactive]
    public partial string FirstName { get; set; }

    [Reactive]
    public partial string LastName { get; set; }

    [Reactive]
    public partial int Age { get; set; }

    [Reactive] 
    internal partial string Tag { get; private set; }
}
