using System;
using Avalonia.Reactive;

namespace MultiTargetingDemo.ViewModels;

[Reactive]
public partial class ViewModelBase
{
}

public partial class MainViewModel : ViewModelBase
{
    [Reactive]
    public partial bool IsInitialized { get; set; }
}

[Reactive]
public partial class LoadingViewModel
{
    public LoadingViewModel()
    {
        this.When
            .IsInitialized
            .Subscribe(new AnonymousObserver<bool>(x =>
            {
                Console.WriteLine($"{nameof(IsInitialized)}={x}");
            }));
    }
    
    public partial bool IsInitialized { get; set; }
}
