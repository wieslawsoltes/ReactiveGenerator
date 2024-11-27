namespace ReactiveGeneratorDemo.ViewModels;

public partial class Student : Person
{
    [Reactive]
    public partial string? Address { get; set; }
}
