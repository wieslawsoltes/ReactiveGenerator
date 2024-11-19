namespace ReactiveGeneratorDemo.ViewModels;

public partial class Test
{
    [Reactive]
    public partial Person Person { get; set; }
    
    [Reactive]
    public partial Car Car { get; set; }
}
