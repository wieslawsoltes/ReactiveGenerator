using System;
using System.Collections.Generic;
using System.Reactive;

namespace ReactiveGeneratorDemo.ViewModels;

public partial class Teacher : Person
{
    public Teacher()
    {
        this.When
            .Students
            .Subscribe(new AnonymousObserver<List<Student>?>(x =>
            {
                Console.WriteLine($"{nameof(Students)} changed");
            }));
    }

    [Reactive]
    public partial List<Student>? Students { get; set; }
}
