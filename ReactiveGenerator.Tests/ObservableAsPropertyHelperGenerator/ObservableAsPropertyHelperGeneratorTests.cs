namespace ReactiveGenerator.Tests;

public class ObservableAsPropertyHelperGeneratorTests
{
    private Task TestAndVerify(string source, Dictionary<string, string>? analyzerConfigOptions = null)
    {
        return SourceGeneratorTestHelper.TestAndVerify(
            source,
            analyzerConfigOptions,
            generators: new ObservableAsPropertyHelperGenerator());
    }

    [Fact]
    public Task SimpleObservableAsPropertyTest()
    {
        var source = @"
            using ReactiveUI;
            
            public partial class TestViewModel : ReactiveObject
            {
                [ObservableAsProperty]
                public partial string ComputedValue { get; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task MultipleObservableAsPropertiesTest()
    {
        var source = @"
            using ReactiveUI;
            
            public partial class TestViewModel : ReactiveObject
            {
                [ObservableAsProperty]
                public partial string FirstValue { get; }

                [ObservableAsProperty]
                public partial int SecondValue { get; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task GenericClassWithObservableAsPropertyTest()
    {
        var source = @"
            using ReactiveUI;
            
            public partial class TestViewModel<T> : ReactiveObject
                where T : class
            {
                [ObservableAsProperty]
                public partial T? Value { get; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task NestedClassWithObservableAsPropertyTest()
    {
        var source = @"
            using ReactiveUI;
            
            public partial class Container
            {
                public partial class NestedViewModel : ReactiveObject
                {
                    [ObservableAsProperty]
                    public partial string Value { get; }
                }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task DifferentAccessibilityLevelsTest()
    {
        var source = @"
            using ReactiveUI;
            
            public partial class TestViewModel : ReactiveObject
            {
                [ObservableAsProperty]
                public partial string PublicValue { get; }

                [ObservableAsProperty]
                protected partial string ProtectedValue { get; }

                [ObservableAsProperty]
                private partial string PrivateValue { get; }

                [ObservableAsProperty]
                internal partial string InternalValue { get; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ComplexTypesTest()
    {
        var source = @"
            using ReactiveUI;
            using System.Collections.Generic;
            
            public partial class TestViewModel : ReactiveObject
            {
                [ObservableAsProperty]
                public partial List<string> StringList { get; }

                [ObservableAsProperty]
                public partial Dictionary<int, string> IntStringMap { get; }

                [ObservableAsProperty]
                public partial (string Name, int Age) PersonInfo { get; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task InheritanceTest()
    {
        var source = @"
            using ReactiveUI;
            
            public partial class BaseViewModel : ReactiveObject
            {
                [ObservableAsProperty]
                public partial string BaseValue { get; }
            }

            public partial class DerivedViewModel : BaseViewModel
            {
                [ObservableAsProperty]
                public partial string DerivedValue { get; }
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
                [ObservableAsProperty]
                public partial string? NullableString { get; }

                [ObservableAsProperty]
                public partial int? NullableInt { get; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task MultipleNamespacesTest()
    {
        var source = @"
            using ReactiveUI;
            
            namespace First
            {
                public partial class TestViewModel : ReactiveObject
                {
                    [ObservableAsProperty]
                    public partial string Value { get; }
                }
            }

            namespace Second
            {
                public partial class TestViewModel : ReactiveObject
                {
                    [ObservableAsProperty]
                    public partial string Value { get; }
                }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task NestedGenericTypesTest()
    {
        var source = @"
            using ReactiveUI;
            using System.Collections.Generic;
            
            public partial class TestViewModel<T> : ReactiveObject
                where T : class
            {
                [ObservableAsProperty]
                public partial List<Dictionary<string, T>> ComplexProperty { get; }

                [ObservableAsProperty]
                public partial Dictionary<int, List<T?>> NestedProperty { get; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task MultiLevelInheritanceTest()
    {
        var source = @"
            using ReactiveUI;
            
            public partial class GrandParentViewModel : ReactiveObject
            {
                [ObservableAsProperty]
                public partial string GrandParentValue { get; }
            }

            public partial class ParentViewModel : GrandParentViewModel
            {
                [ObservableAsProperty]
                public partial string ParentValue { get; }
            }

            public partial class ChildViewModel : ParentViewModel
            {
                [ObservableAsProperty]
                public partial string ChildValue { get; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task MixedAttributesTest()
    {
        var source = @"
            using ReactiveUI;
            
            public partial class TestViewModel : ReactiveObject
            {
                [ObservableAsProperty]
                public partial string ComputedValue { get; }

                [Reactive]
                public partial string EditableValue { get; set; }

                public string NonReactiveValue { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task NestedPrivateClassTest()
    {
        var source = @"
            using ReactiveUI;
            
            public partial class Container
            {
                private partial class PrivateViewModel : ReactiveObject
                {
                    [ObservableAsProperty]
                    public partial string Value { get; }
                }

                protected partial class ProtectedViewModel : ReactiveObject
                {
                    [ObservableAsProperty]
                    public partial string Value { get; }
                }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task GenericConstraintsTest()
    {
        var source = @"
            using ReactiveUI;
            
            public partial class TestViewModel<T, U> : ReactiveObject
                where T : class, new()
                where U : struct, IComparable<U>
            {
                [ObservableAsProperty]
                public partial T? ReferenceValue { get; }

                [ObservableAsProperty]
                public partial U ValueType { get; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task StaticPropertyTest()
    {
        var source = @"
            using ReactiveUI;
            
            public partial class TestViewModel : ReactiveObject
            {
                [ObservableAsProperty]
                public static partial string StaticValue { get; }

                [ObservableAsProperty]
                public partial string InstanceValue { get; }
            }";

        return TestAndVerify(source);
    }
  
    [Fact]
    public Task ObservableAsPropertyWithConstraints()
    {
        var source = @"
        using ReactiveUI;
        
        public partial class ViewModel<T, TKey> : ReactiveObject
            where T : class, IDisposable
            where TKey : notnull
        {
            [ObservableAsProperty]
            public partial T? ComputedValue { get; }

            [ObservableAsProperty]
            public partial TKey CurrentKey { get; }
        }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ObservableAsPropertyWithComplexConstraints()
    {
        var source = @"
        using ReactiveUI;
        
        public partial class Container<T, U, V> : ReactiveObject
            where T : class?
            where U : struct, IComparable<U>
            where V : unmanaged
        {
            [ObservableAsProperty]
            public partial T? NullableRef { get; }

            [ObservableAsProperty]
            public partial U ValueType { get; }

            [ObservableAsProperty]
            public partial V UnmanagedValue { get; }
        }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ObservableAsPropertyWithNestedGenerics()
    {
        var source = @"
        using ReactiveUI;
        
        public partial class Outer<T> : ReactiveObject
            where T : class
        {
            public partial class Inner<U> : ReactiveObject
                where U : T, new()
            {
                [ObservableAsProperty]
                public partial U? Value { get; }
            }
        }";

        return TestAndVerify(source);
    }
}
