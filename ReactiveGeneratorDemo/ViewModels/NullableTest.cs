using System;

namespace ReactiveGeneratorDemo.ViewModels;

public partial class NullableTest
{
    public NullableTest()
    {
        
    }
    
    [Reactive]
    public partial DateTime? StartDate { get; set; }

    [Reactive]
    public partial DateTime? EndDate { get; set; }
}
