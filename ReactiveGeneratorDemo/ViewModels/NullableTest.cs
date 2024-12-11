using System;

namespace ReactiveGeneratorDemo.ViewModels;

public partial class NullableTest
{
    public NullableTest()
    {
        this.WhenAnyStartDate()
            .Subscribe(x => Console.WriteLine($"Start: {x}"));

        this.WhenAnyEndDate()
            .Subscribe(x => Console.WriteLine($"End: {x}"));
    }

    [Reactive]
    public partial DateTime? StartDate { get; set; }

    [Reactive]
    public partial DateTime? EndDate { get; set; }
}
