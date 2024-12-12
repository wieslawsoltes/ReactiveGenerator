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
