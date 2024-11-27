using System.Collections.Generic;

namespace ReactiveGeneratorDemo.ViewModels;

public partial class Teacher : Person
{
    [Reactive]
    public partial List<Student>? Students { get; set; }
}
