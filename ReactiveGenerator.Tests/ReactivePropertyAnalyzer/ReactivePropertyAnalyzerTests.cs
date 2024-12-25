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
    
    private Task TestAndVerifyWithFix(string source, string equivalenceKey)
    {
        return AnalyzerTestHelper.TestAndVerifyWithFix(
            source,
            equivalenceKey,
            analyzers: new[] { new ReactivePropertyAnalyzer() });
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

    [Fact]
    public Task SinglePropertyFixTest()
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

        return TestAndVerifyWithFix(source, "ReactivePropertyCodeFixProvider_Single");
    }

    [Fact]
    public Task DocumentWideFixTest()
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

                public string NonReactiveProperty { get; set; }
            }";

        return TestAndVerifyWithFix(source, "ReactivePropertyCodeFixProvider_Document");
    }

    [Fact]
    public Task ProjectWideFixTest()
    {
        var source = @"
            using ReactiveUI;
            
            public partial class FirstViewModel : ReactiveObject
            {
                private string _name;
                public string Name
                {
                    get => _name;
                    set => this.RaiseAndSetIfChanged(ref _name, value);
                }
            }

            public partial class SecondViewModel : ReactiveObject
            {
                private int _count;
                public int Count
                {
                    get => _count;
                    set => this.RaiseAndSetIfChanged(ref _count, value);
                }

                public string NonReactiveProperty { get; set; }
            }";

        return TestAndVerifyWithFix(source, "ReactivePropertyCodeFixProvider_Project");
    }

    [Fact]
    public Task SolutionWideFixTest()
    {
        var source = @"
            using ReactiveUI;
            
            public partial class FirstViewModel : ReactiveObject
            {
                private string _name;
                public string Name
                {
                    get => _name;
                    set => this.RaiseAndSetIfChanged(ref _name, value);
                }
            }

            public partial class SecondViewModel : ReactiveObject
            {
                private int _count;
                public int Count
                {
                    get => _count;
                    set => this.RaiseAndSetIfChanged(ref _count, value);
                }
            }";

        return TestAndVerifyWithFix(source, "ReactivePropertyCodeFixProvider_Solution");
    }

    [Fact]
    public Task MixedPropertyTypesFixTest()
    {
        var source = @"
            using ReactiveUI;
            using System;
            using System.Windows.Input;
            
            public partial class TestViewModel : ReactiveObject
            {
                private string _name;
                public string Name
                {
                    get => _name;
                    set => this.RaiseAndSetIfChanged(ref _name, value);
                }

                public ICommand UpdateCommand { get; set; }
                public IObservable<int> Values { get; }
                public string ReadOnlyProperty { get; }

                private int _count;
                public int Count
                {
                    get => _count;
                    set => this.RaiseAndSetIfChanged(ref _count, value);
                }
            }";

        return TestAndVerifyWithFix(source, "ReactivePropertyCodeFixProvider_Document");
    }

    [Fact]
    public Task CustomAccessibilityFixTest()
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

        return TestAndVerifyWithFix(source, "ReactivePropertyCodeFixProvider_Single");
    }

    [Fact]
    public Task PreservesExistingReactivesFixTest()
    {
        var source = @"
            using ReactiveUI;
            
            public partial class TestViewModel : ReactiveObject
            {
                [Reactive]
                public partial string ExistingReactive { get; set; }

                private string _name;
                public string Name
                {
                    get => _name;
                    set => this.RaiseAndSetIfChanged(ref _name, value);
                }

                public string NonReactive { get; set; }
            }";

        return TestAndVerifyWithFix(source, "ReactivePropertyCodeFixProvider_Document");
    }

    [Fact]
    public Task OnlyConvertsReactiveObjectsFixTest()
    {
        var source = @"
            using ReactiveUI;
            using System.ComponentModel;
            
            public class NonReactiveViewModel : INotifyPropertyChanged
            {
                private string _value;
                public string Value
                {
                    get => _value;
                    set
                    {
                        _value = value;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
                    }
                }

                public event PropertyChangedEventHandler? PropertyChanged;
            }

            public partial class ReactiveViewModel : ReactiveObject
            {
                private string _name;
                public string Name
                {
                    get => _name;
                    set => this.RaiseAndSetIfChanged(ref _name, value);
                }
            }";

        return TestAndVerifyWithFix(source, "ReactivePropertyCodeFixProvider_Document");
    }

    [Fact]
    public Task HandlesGenericsFixTest()
    {
        var source = @"
            using ReactiveUI;
            using System.Collections.ObjectModel;
            
            public partial class TestViewModel<T> : ReactiveObject where T : class
            {
                private ObservableCollection<T>? _items;
                public ObservableCollection<T>? Items
                {
                    get => _items;
                    set => this.RaiseAndSetIfChanged(ref _items, value);
                }
            }";

        return TestAndVerifyWithFix(source, "ReactivePropertyCodeFixProvider_Single");
    }
}
