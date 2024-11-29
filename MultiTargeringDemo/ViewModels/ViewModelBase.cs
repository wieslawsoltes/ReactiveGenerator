namespace MultiTargeringDemo.ViewModels;

[Reactive]
public partial class ViewModelBase
{
}

public partial class MainViewModel : ViewModelBase
{
    [Reactive]
    public partial bool IsInitialized { get; set; }
}
