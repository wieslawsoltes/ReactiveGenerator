using Xunit;

namespace ReactiveGenerator.Tests;

public class ReactivePropertyAnalyzerTests
{
    private Task TestAndVerify(string source, Dictionary<string, string>? analyzerConfigOptions = null)
    {
        return AnalyzerTestHelper.TestAndVerify(
            source,
            analyzerConfigOptions,
            analyzers: new ReactivePropertyAnalyzer());
    }

    [Fact]
    public Task SimpleReactivePropertyTest()
    {
        var source = @"
            using ReactiveUI;
            
            public partial class TestViewModel : ReactiveObject
            {
                private string _value;
                public string Value
                {
                    get => _value;
                    set => this.RaiseAndSetIfChanged(ref _value, value);
                }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task MultipleReactivePropertiesTest()
    {
        var source = @"
            using ReactiveUI;
            
            public partial class TestViewModel : ReactiveObject
            {
                private string _first;
                public string First
                {
                    get => _first;
                    set => this.RaiseAndSetIfChanged(ref _first, value);
                }

                private int _second;
                public int Second
                {
                    get => _second;
                    set => this.RaiseAndSetIfChanged(ref _second, value);
                }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task GenericClassWithReactivePropertyTest()
    {
        var source = @"
            using ReactiveUI;
            
            public partial class TestViewModel<T> : ReactiveObject
                where T : class
            {
                private T? _value;
                public T? Value
                {
                    get => _value;
                    set => this.RaiseAndSetIfChanged(ref _value, value);
                }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task CustomAccessorAccessibilityTest()
    {
        var source = @"
            using ReactiveUI;
            
            public partial class TestViewModel : ReactiveObject
            {
                private string _value;
                public string Value
                {
                    protected get => _value;
                    private set => this.RaiseAndSetIfChanged(ref _value, value);
                }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task NonReactiveObjectShouldNotTriggerAnalyzer()
    {
        var source = @"
            using System.ComponentModel;
            
            public class TestViewModel : INotifyPropertyChanged
            {
                private string _value;
                public string Value
                {
                    get => _value;
                    set => this.RaiseAndSetIfChanged(ref _value, value);
                }

                public event PropertyChangedEventHandler PropertyChanged;
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task PropertyWithoutSetterShouldNotTriggerAnalyzer()
    {
        var source = @"
            using ReactiveUI;
            
            public partial class TestViewModel : ReactiveObject
            {
                private string _value;
                public string Value
                {
                    get => _value;
                }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task NullablePropertiesTest()
    {
        var source = @"
            using ReactiveUI;
            
            public partial class TestViewModel : ReactiveObject
            {
                private string? _nullableString;
                public string? NullableString
                {
                    get => _nullableString;
                    set => this.RaiseAndSetIfChanged(ref _nullableString, value);
                }

                private int? _nullableInt;
                public int? NullableInt
                {
                    get => _nullableInt;
                    set => this.RaiseAndSetIfChanged(ref _nullableInt, value);
                }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task BackingFieldUsedElsewhereTest()
    {
        var source = @"
            using ReactiveUI;
            
            public partial class TestViewModel : ReactiveObject
            {
                private string _value;
                public string Value
                {
                    get => _value;
                    set => this.RaiseAndSetIfChanged(ref _value, value);
                }

                public string GetValueDirectly() => _value;
            }";

        return TestAndVerify(source);
    }
}
