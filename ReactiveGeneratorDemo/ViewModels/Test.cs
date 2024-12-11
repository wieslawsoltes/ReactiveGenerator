namespace ReactiveGeneratorDemo.ViewModels;

public partial class Test
{
    [Reactive]
    public partial Person? Person { get; set; }
}

public partial class Test
{
    [Reactive]
    public partial Car? Car { get; set; }

    [Reactive]
    public partial OaphViewModel<string>? OaphViewModel { get; set; }
    
}
