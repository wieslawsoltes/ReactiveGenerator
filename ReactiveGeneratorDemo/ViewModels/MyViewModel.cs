using System.Reactive.Linq;
using ReactiveGenerator;
using ReactiveUI;

namespace ReactiveGeneratorDemo.ViewModels;

public partial class MyViewModel : ReactiveObject
{
    public MyViewModel()
    {
        this.WhenAnyCounter()
            .Select(x => $"Counter: {x}")
            .ToProperty(this, x => x.ComputedValue, out _computedValueHelper);
    }

    [ObservableAsProperty]
    public partial string ComputedValue { get; }
    
    [Reactive]
    public partial int Counter { get; set; }
}
