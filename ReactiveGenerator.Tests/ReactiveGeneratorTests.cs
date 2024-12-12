using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ReactiveGenerator.Tests;

public class ReactiveGeneratorTests
{
    [Fact]
    public Task SimpleClassWithReactiveAttribute()
    {
        var source = @"
            [Reactive]
            public partial class TestClass
            {
                public partial string Name { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ClassWithReactiveObjectInheritance()
    {
        var source = @"
            using ReactiveUI;
            
            [Reactive]
            public partial class TestClass : ReactiveObject
            {
                public partial string Name { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ClassWithIgnoreReactiveAttribute()
    {
        var source = @"
            [Reactive]
            public partial class TestClass
            {
                [IgnoreReactive]
                public partial string Ignored { get; set; }
                public partial string Included { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ClassWithDifferentPropertyAccessibilities()
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
    public Task ClassWithReadOnlyProperties()
    {
        var source = @"
            [Reactive]
            public partial class TestClass
            {
                public partial string ReadOnly { get; }
                public partial string ReadWrite { get; set; }
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
    public Task ClassWithCustomAccessorAccessibility()
    {
        var source = @"
            [Reactive]
            public partial class TestClass
            {
                public partial string Name { get; private set; }
                protected partial string Id { private get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ClassWithPropertyImplementation()
    {
        var source = @"
            [Reactive]
            public partial class TestClass
            {
                public partial string Implemented { get => ""test""; }
                public partial string NotImplemented { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ClassWithNestedTypes()
    {
        var source = @"
            public partial class OuterClass
            {
                [Reactive]
                public partial class InnerClass
                {
                    public partial string Name { get; set; }
                }
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
    public Task UseLegacyModeTest()
    {
        var source = @"
            [Reactive]
            public partial class TestClass
            {
                public partial string Name { get; set; }
            }";

        var analyzerConfigOptions = new Dictionary<string, string>
        {
            ["build_property.UseBackingFields"] = "true"
        };

        return TestAndVerify(source, analyzerConfigOptions);
    }

    [Fact]
    public Task ClassInGlobalNamespace()
    {
        var source = @"
            [Reactive]
            public partial class GlobalClass
            {
                public partial string Name { get; set; }
            }";

        return TestAndVerify(source);
    }
    
    [Fact]
    public Task ClassWithMultipleReactiveAttributes()
    {
        var source = @"
            [Reactive]
            public partial class TestClass
            {
                [Reactive]
                public partial string DoubleDeclared { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ClassWithCustomPropertyImplementationAndReactiveAttribute()
    {
        var source = @"
            [Reactive]
            public partial class TestClass
            {
                private string _customImpl = string.Empty;
                [Reactive]
                public partial string CustomImpl 
                { 
                    get => _customImpl;
                    set => _customImpl = value;
                }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ClassWithExplicitInterfaceImplementation()
    {
        var source = @"
            public interface IHasName
            {
                string Name { get; set; }
            }

            [Reactive]
            public partial class TestClass : IHasName
            {
                string IHasName.Name { get; set; }
                public partial string PublicName { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task NestedClassWithInheritance()
    {
        var source = @"
            public partial class OuterClass
            {
                [Reactive]
                public partial class InnerBase
                {
                    public partial string BaseProp { get; set; }
                }

                [Reactive]
                public partial class InnerDerived : InnerBase
                {
                    public partial string DerivedProp { get; set; }
                }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ClassWithGenericConstraintsAndNullableProperties()
    {
        var source = @"
            [Reactive]
            public partial class TestClass<T, U> 
                where T : class?, IDisposable
                where U : struct, IComparable<U>
            {
                public partial T? NullableRef { get; set; }
                public partial U? NullableStruct { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ClassWithAbstractProperties()
    {
        var source = @"
            [Reactive]
            public abstract partial class TestClass
            {
                public abstract string AbstractProp { get; set; }
                public partial string ConcreteProp { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ClassWithPropertyAccessorModifiers()
    {
        var source = @"
            [Reactive]
            public partial class TestClass
            {
                public partial string PropWithPrivateSet { get; private set; }
                protected partial string PropWithProtectedGet { private get; set; }
                internal partial string PropWithInternalSet { get; internal set; }
                public partial string PropWithProtectedInternalSet { get; protected internal set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ClassWithVirtualProperties()
    {
        var source = @"
            [Reactive]
            public partial class TestClass
            {
                public virtual partial string VirtualProp { get; set; }
                public partial string NonVirtualProp { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ClassWithStaticProperties()
    {
        var source = @"
            [Reactive]
            public partial class TestClass
            {
                public static partial string StaticProp { get; set; }
                public partial string InstanceProp { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ClassWithIndexers()
    {
        var source = @"
            [Reactive]
            public partial class TestClass
            {
                private string[] _items = new string[10];
                
                public string this[int index]
                {
                    get => _items[index];
                    set => _items[index] = value;
                }

                public partial string RegularProp { get; set; }
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
    public Task ClassWithMultipleInheritanceLevels()
    {
        var source = @"
            public partial class GrandParent
            {
                public virtual string GrandParentProp { get; set; }
            }

            [Reactive]
            public partial class Parent : GrandParent
            {
                public partial string ParentProp { get; set; }
            }

            [Reactive]
            public partial class Child : Parent
            {
                public partial string ChildProp { get; set; }
                public override string GrandParentProp { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ClassWithReactiveAttributeInDifferentNamespace()
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
    public Task ClassWithExpressionBodiedProperties()
    {
        var source = @"
            [Reactive]
            public partial class TestClass
            {
                private string _name = string.Empty;
                public string ComputedName => _name.ToUpper();
                public partial string EditableName { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ClassWithTupleProperties()
    {
        var source = @"
            [Reactive]
            public partial class TestClass
            {
                public partial (string Name, int Age) PersonInfo { get; set; }
                public partial (int X, int Y, string Label) Point { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ClassWithInitOnlySetters()
    {
        var source = @"
            [Reactive]
            public partial class TestClass
            {
                public partial string InitOnlyProp { get; init; }
                public partial string RegularProp { get; set; }
            }";

        return TestAndVerify(source);
    }
    
    private static Task TestAndVerify(
        string source,
        Dictionary<string, string>? analyzerConfigOptions = null,
        [CallerMemberName] string? testName = null)
    {
        // Create compilation
        var compilation = CreateCompilation(source);
        
        // Create generator instance
        var generator = new ReactiveGenerator();
        
        // Create the driver with generator and options
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() },
            parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options,
            optionsProvider: analyzerConfigOptions != null 
                ? new DictionaryAnalyzerConfigOptionsProvider(analyzerConfigOptions) 
                : null);

        // Run generation
        driver = driver.RunGenerators(compilation);

        // Use our new verify helper
        return TestHelpers.Verify(driver);
    }

    private static Compilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(INotifyPropertyChanged).Assembly.Location),
            // Add ReactiveUI reference if needed for your tests
            // MetadataReference.CreateFromFile(typeof(ReactiveObject).Assembly.Location)
        };

        var compilation = CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return compilation;
    }

    private class DictionaryAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly DictionaryAnalyzerConfigOptions _options;

        public DictionaryAnalyzerConfigOptionsProvider(Dictionary<string, string> options)
        {
            _options = new DictionaryAnalyzerConfigOptions(options);
        }

        public override AnalyzerConfigOptions GlobalOptions => _options;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _options;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _options;
    }

    private class DictionaryAnalyzerConfigOptions : AnalyzerConfigOptions
    {
        private readonly Dictionary<string, string> _options;

        public DictionaryAnalyzerConfigOptions(Dictionary<string, string> options)
        {
            _options = options;
        }

        public override bool TryGetValue(string key, out string value)
            => _options.TryGetValue(key, out value!);
    }
}
