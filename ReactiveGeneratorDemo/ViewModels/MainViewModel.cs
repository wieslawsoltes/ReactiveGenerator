namespace ReactiveGeneratorDemo.ViewModels;

// TODO: Edge case base class with [Reactive] properties
// [Reactive]
public partial class MainViewModel : ViewModelBase
{
    [Reactive]
    public partial object? Data { get; set; }
}
