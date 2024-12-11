using System.Reactive;
using ReactiveUI;

namespace ReactiveGeneratorDemo.ViewModels;

public abstract partial class ValidatingViewModelBase<T> : ReactiveObject where T : class;

public abstract partial class ValidatingViewModel<T> : ValidatingViewModelBase<T>
    where T : class
{
    [Reactive] public partial ReactiveCommand<Unit, Unit> PerformInitializeCommand { get; protected set; }
}
