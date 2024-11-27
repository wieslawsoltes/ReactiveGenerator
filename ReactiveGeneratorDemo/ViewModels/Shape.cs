namespace ReactiveGeneratorDemo.ViewModels;

[Reactive]
public partial class Shape
{
    public partial string? Name { get; set; }

    [IgnoreReactive]
    public partial string? Tag { get; set; }
}

public partial class Shape
{
    private string? _tag;

    public partial string? Tag
    {
        get => _tag; 
        set => _tag = value;
    }
}
