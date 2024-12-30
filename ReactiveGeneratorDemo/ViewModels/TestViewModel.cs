using System;
using ReactiveGenerator;

namespace ReactiveGeneratorDemo.ViewModels;

[Reactive]
public partial class TestViewModel
{
    [GenerateCommand]
    private void DoSomething()
    {
        Console.WriteLine("Hello World");
    }
}
