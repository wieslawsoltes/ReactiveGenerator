namespace ReactiveGeneratorDemo.ViewModels;

[Reactive]
public partial class ViewModelBase
{
    [Reactive]
    public partial bool IsInitialized { get; set; }
}

[Reactive]
public partial class MainViewModel : ViewModelBase
{
    [Reactive]
    public partial object? Data { get; set; }
}
