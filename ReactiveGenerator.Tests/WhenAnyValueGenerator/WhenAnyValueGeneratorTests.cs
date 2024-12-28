namespace ReactiveGenerator.Tests;

public class WhenAnyValueGeneratorTests
{
    private Task TestAndVerify(string source, Dictionary<string, string>? analyzerConfigOptions = null)
    {
        return SourceGeneratorTestHelper.TestAndVerify(
            source, 
            analyzerConfigOptions, 
            generators: new WhenAnyValueGenerator());
    }

    [Fact]
    public Task SimpleClassWithReactiveAttribute()
    {
        var source = @"
            [Reactive]
            public partial class TestClass
            {
                public partial string Name { get; set; }
                public partial int Age { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ClassWithPropertyLevelReactiveAttribute()
    {
        var source = @"
            public partial class TestClass
            {
                [Reactive]
                public partial string Name { get; set; }
                
                [Reactive]
                public partial int Age { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ClassWithMixedReactiveAttributes()
    {
        var source = @"
            [Reactive]
            public partial class TestClass
            {
                public partial string Name { get; set; }
                
                [IgnoreReactive]
                public partial int Ignored { get; set; }
                
                [Reactive]
                public partial DateTime ExplicitReactive { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task GenericClassWithConstraints()
    {
        var source = @"
            [Reactive]
            public partial class TestClass<T, U>
                where T : class, new()
                where U : struct
            {
                public partial T Reference { get; set; }
                public partial U Value { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ClassWithNullableProperties()
    {
        var source = @"
            [Reactive]
            public partial class TestClass
            {
                public partial string? NullableRef { get; set; }
                public partial int? NullableValue { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task NestedClassWithReactiveProperties()
    {
        var source = @"
            public partial class OuterClass
            {
                [Reactive]
                public partial class InnerClass
                {
                    public partial string Name { get; set; }
                    public partial int Value { get; set; }
                }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ClassWithDifferentAccessibilities()
    {
        var source = @"
            [Reactive]
            public partial class TestClass
            {
                public partial string PublicProp { get; set; }
                protected partial string ProtectedProp { get; set; }
                internal partial string InternalProp { get; set; }
                private partial string PrivateProp { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ClassWithComplexPropertyTypes()
    {
        var source = @"
            using System.Collections.Generic;

            [Reactive]
            public partial class TestClass
            {
                public partial List<string> StringList { get; set; }
                public partial Dictionary<int, string> IntStringMap { get; set; }
                public partial (string Name, int Age) PersonInfo { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task MultipleClassesInDifferentNamespaces()
    {
        var source = @"
            namespace First
            {
                [Reactive]
                public partial class TestClass
                {
                    public partial string Name { get; set; }
                }
            }

            namespace Second
            {
                [Reactive]
                public partial class TestClass
                {
                    public partial string Name { get; set; }
                }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ClassInheritingFromReactiveObject()
    {
        var source = @"
            using ReactiveUI;
            
            [Reactive]
            public partial class TestClass : ReactiveObject
            {
                public partial string Name { get; set; }
                public partial int Age { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ClassWithNestedGenerics()
    {
        var source = @"
            [Reactive]
            public partial class TestClass<T>
                where T : class
            {
                public partial List<Dictionary<string, T>> ComplexProp { get; set; }
                public partial Dictionary<int, List<T?>> NestedProp { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task MultipleClassesWithInheritance()
    {
        var source = @"
            [Reactive]
            public partial class BaseClass
            {
                public partial string BaseProp { get; set; }
            }

            [Reactive]
            public partial class DerivedClass : BaseClass
            {
                public partial string DerivedProp { get; set; }
            }";

        return TestAndVerify(source);
    }
    
    [Fact]
    public Task InheritedReactiveAttribute()
    {
        var source = @"
            [Reactive]
            public abstract partial class BaseViewModel
            {
                public partial string BaseProp { get; set; }
            }

            public partial class ViewModel : BaseViewModel
            {
                public partial string ViewModelProp { get; set; }
            }

            [IgnoreReactive]
            public partial class NonReactiveViewModel : BaseViewModel
            {
                public partial string IgnoredProp { get; set; }
            }

            public partial class DerivedViewModel : ViewModel
            {
                public partial string DerivedProp { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task MultiLevelInheritanceWithMixedReactiveAttributes()
    {
        var source = @"
            [Reactive]
            public abstract partial class BaseViewModel
            {
                public partial string BaseProp { get; set; }
            }

            public partial class MiddleViewModel : BaseViewModel
            {
                public partial string MiddleProp { get; set; }
                
                [IgnoreReactive]
                public partial string IgnoredMiddleProp { get; set; }
            }

            [IgnoreReactive]
            public partial class NonReactiveViewModel : MiddleViewModel
            {
                public partial string NonReactiveProp { get; set; }
                
                [Reactive]
                public partial string StillReactiveProp { get; set; }
            }

            [Reactive]
            public partial class ReactiveDerivedViewModel : NonReactiveViewModel
            {
                public partial string ReactiveProp { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task CrossAssemblyInheritanceTest()
    {
        var baseSource = @"
            namespace ExternalLib
            {
                [Reactive]
                public partial class ExternalBase
                {
                    public partial string ExternalProp { get; set; }
                }
            }";

        var derivedSource = @"
            using ExternalLib;

            namespace MainLib
            {
                public partial class DerivedClass : ExternalBase
                {
                    [Reactive]
                    public partial string LocalProp { get; set; }
                }

                [IgnoreReactive]
                public partial class NonReactiveDerived : ExternalBase
                {
                    public partial string IgnoredProp { get; set; }
                    
                    [Reactive]
                    public partial string StillReactiveProp { get; set; }
                }
            }";

        return SourceGeneratorTestHelper.TestCrossAssemblyAndVerifyWithExternalGen(
            baseSource,
            derivedSource,
            null,
            new WhenAnyValueGenerator());
    }

    [Fact]
    public Task GenericInheritanceWithReactiveAttribute()
    {
        var source = @"
            [Reactive]
            public abstract partial class BaseViewModel<T>
                where T : class
            {
                public partial T? Value { get; set; }
            }

            public partial class StringViewModel : BaseViewModel<string>
            {
                public partial string AdditionalProp { get; set; }
            }

            [IgnoreReactive]
            public partial class NonReactiveViewModel<T> : BaseViewModel<T>
                where T : class
            {
                public partial string IgnoredProp { get; set; }
                
                [Reactive]
                public partial T? StillReactiveProp { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task NestedInheritanceWithReactiveAttribute()
    {
        var source = @"
            public partial class Container
            {
                [Reactive]
                public abstract partial class NestedBase
                {
                    public partial string BaseProp { get; set; }
                }

                public partial class NestedDerived : NestedBase
                {
                    public partial string DerivedProp { get; set; }
                }

                [IgnoreReactive]
                public partial class NonReactiveNested : NestedBase
                {
                    public partial string IgnoredProp { get; set; }
                    
                    [Reactive]
                    public partial string StillReactiveProp { get; set; }
                }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task InterfaceImplementationWithReactiveAttribute()
    {
        var source = @"
            public interface IHasValue<T>
            {
                T Value { get; set; }
            }

            [Reactive]
            public abstract partial class ReactiveBase<T> : IHasValue<T>
            {
                public partial T Value { get; set; }
            }

            public partial class Implementation : ReactiveBase<string>
            {
                public partial string ExtraProp { get; set; }
            }

            [IgnoreReactive]
            public partial class NonReactiveImpl : ReactiveBase<int>
            {
                public partial string IgnoredProp { get; set; }
                
                [Reactive]
                public partial int ExtraValue { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task MixedPropertyAndClassLevelReactiveInheritance()
    {
        var source = @"
            public partial class BaseClass
            {
                [Reactive]
                public partial string ReactiveBaseProp { get; set; }
            }

            [Reactive]
            public partial class DerivedClass : BaseClass
            {
                public partial string DerivedProp { get; set; }
                
                [IgnoreReactive]
                public partial string IgnoredProp { get; set; }
            }

            public partial class GrandChild : DerivedClass
            {
                [Reactive]
                public partial string ExplicitReactiveProp { get; set; }
                public partial string InheritedReactiveProp { get; set; }
            }";

        return TestAndVerify(source);
    }
}
