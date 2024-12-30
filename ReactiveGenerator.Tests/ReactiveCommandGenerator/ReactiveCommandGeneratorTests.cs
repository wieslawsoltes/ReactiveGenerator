namespace ReactiveGenerator.Tests;

public class ReactiveCommandGeneratorTests
{
    private Task TestAndVerify(string source, Dictionary<string, string>? analyzerConfigOptions = null)
    {
        return SourceGeneratorTestHelper.TestAndVerify(
            source,
            analyzerConfigOptions,
            generators: new ReactiveCommandGenerator());
    }

    // Simple synchronous method with no parameters => should generate a ReactiveCommand<Unit, Unit>.
    [Fact]
    public Task SynchronousCommand_NoParams()
    {
        var source = @"
                using System;
                using ReactiveGenerator;

                public partial class TestViewModel
                {
                    [GenerateCommand(CommandName = ""DoSomethingCommand"")]
                    public void DoSomething()
                    {
                        Console.WriteLine(""Hello World"");
                    }
                }";

        return TestAndVerify(source);
    }

    // Async method (returning Task) => should generate CreateFromTask<Unit, Unit>.
    [Fact]
    public Task AsyncCommand_NoParams()
    {
        var source = @"
                using System;
                using System.Threading.Tasks;
                using ReactiveGenerator;

                public partial class TestViewModel
                {
                    [GenerateCommand]
                    public async Task SaveDataAsync()
                    {
                        await Task.Delay(10);
                    }
                }";

        // Command property defaults to 'SaveDataAsyncCommand'
        return TestAndVerify(source);
    }

    // Async method returning Task<T> with one parameter => should generate CreateFromTask<TParam, TResult>.
    [Fact]
    public Task AsyncCommand_WithParam_ReturnsTaskOfT()
    {
        var source = @"
                using System.Threading.Tasks;
                using ReactiveGenerator;

                public partial class TestViewModel
                {
                    [GenerateCommand(CommandName = ""ComputeCommand"")]
                    public async Task<int> ComputeValueAsync(int x)
                    {
                        await Task.Delay(10);
                        return x * 2;
                    }
                }";

        return TestAndVerify(source);
    }

    // Method returning IObservable<T> => should generate CreateFromObservable<Unit, T>.
    [Fact]
    public Task ObservableCommand_NoParams()
    {
        var source = @"
                using System;
                using ReactiveGenerator;
                using System.Reactive.Linq;

                public partial class TestViewModel
                {
                    [GenerateCommand(CommandName = ""FetchDataCommand"")]
                    public IObservable<string> FetchData()
                    {
                        return Observable.Return(""Hello from an observable!"");
                    }
                }";

        return TestAndVerify(source);
    }

    // Method with one parameter returning IObservable<T> => CreateFromObservable<TParam, TResult>.
    [Fact]
    public Task ObservableCommand_WithParam()
    {
        var source = @"
                using System;
                using ReactiveGenerator;
                using System.Reactive.Linq;

                public partial class TestViewModel
                {
                    [GenerateCommand]
                    public IObservable<int> GenerateNumbers(int count)
                    {
                        return Observable.Range(1, count);
                    }
                }";

        // Command property => "GenerateNumbersCommand"
        return TestAndVerify(source);
    }

    // Synchronous method with one parameter => should generate Create<TParam, Unit>.
    [Fact]
    public Task SynchronousCommand_WithParam()
    {
        var source = @"
                using System;
                using ReactiveGenerator;

                public partial class TestViewModel
                {
                    [GenerateCommand(CommandName = ""LogMessageCommand"")]
                    public void LogMessage(string message)
                    {
                        Console.WriteLine(""Logged: "" + message);
                    }
                }";

        return TestAndVerify(source);
    }

    // Command with a CanExecute property referencing a bool field or property => uses this.WhenAnyValue(...)
    [Fact]
    public Task Command_WithCanExecuteProperty()
    {
        var source = @"
                using System;
                using ReactiveGenerator;
                using System.Reactive;
                using ReactiveUI;

                public partial class TestViewModel : ReactiveObject
                {
                    private bool _canSave = true;
                    public bool CanSave
                    {
                        get => _canSave;
                        set => this.RaiseAndSetIfChanged(ref _canSave, value);
                    }

                    [GenerateCommand(CommandName = ""SaveCommand"", CanExecuteProperty = nameof(CanSave))]
                    public void SaveData()
                    {
                        Console.WriteLine(""Data Saved"");
                    }
                }";

        return TestAndVerify(source);
    }

    // Command with a CanExecute property referencing an IObservable<bool> => used directly.
    [Fact]
    public Task Command_WithCanExecuteObservable()
    {
        var source = @"
                using System;
                using System.Reactive.Linq;
                using ReactiveGenerator;

                public partial class TestViewModel
                {
                    // A field of type IObservable<bool>, or property. We'll just do a field for demonstration.
                    public IObservable<bool> CanRunObservable = Observable.Return(true);

                    [GenerateCommand(
                        CommandName = ""RunSomethingCommand"", 
                        CanExecuteProperty = nameof(CanRunObservable))]
                    public void RunSomething()
                    {
                        Console.WriteLine(""Running something..."");
                    }
                }";

        return TestAndVerify(source);
    }

    // Demonstrate user specifying a ParameterType that differs from method param.
    // Useful when you want a custom aggregator or if the method has multiple params (which we haven't fully implemented).
    [Fact]
    public Task Command_WithCustomParameterType()
    {
        var source = @"
                using System;
                using ReactiveGenerator;

                public class SomeDto
                {
                    public int X { get; set; }
                    public int Y { get; set; }
                }

                public partial class TestViewModel
                {
                    [GenerateCommand(ParameterType = typeof(SomeDto))]
                    public void DoWork(int x, int y)
                    {
                        Console.WriteLine($""Doing work with {x}, {y}"");
                    }
                }";

        // We won't properly handle the multiple param scenario in the generator's code,
        // but this at least shows how specifying ParameterType is recognized and won't cause an error in the test.
        return TestAndVerify(source);
    }
}
