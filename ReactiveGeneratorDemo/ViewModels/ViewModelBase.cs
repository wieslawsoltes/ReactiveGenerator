namespace ReactiveGeneratorDemo.ViewModels;

[Reactive]
public partial class ViewModelBase
{
    // TODO: Edge case no [Reactive] properties
    // [Reactive]
    // public partial bool IsInitialized { get; set; }
}

// TODO: Edge case base class with [Reactive] properties
// [Reactive]
public partial class MainViewModel : ViewModelBase
{
    [Reactive]
    public partial object? Data { get; set; }
}
