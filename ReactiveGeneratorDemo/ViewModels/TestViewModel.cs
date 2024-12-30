using System;
using System.Threading.Tasks;

namespace ReactiveGeneratorDemo.ViewModels;

[Reactive]
public partial class TestViewModel
{
    [GenerateCommand]
    private void DoSomething()
    {
        Console.WriteLine("Hello World");
    }
    
    [GenerateCommand]
    private async Task DoSomethingAsync()
    {
        Console.WriteLine("Hello World");
    }
}
