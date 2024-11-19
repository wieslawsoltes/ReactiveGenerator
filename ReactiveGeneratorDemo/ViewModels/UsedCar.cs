namespace ReactiveGeneratorDemo.ViewModels;

public partial class UsedCar : Car
{
    [Reactive]
    public partial decimal Price { get; private set; }

    [Reactive]
    public partial int Miles { get; set; }
}
