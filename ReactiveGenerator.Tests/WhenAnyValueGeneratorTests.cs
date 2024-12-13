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
}
