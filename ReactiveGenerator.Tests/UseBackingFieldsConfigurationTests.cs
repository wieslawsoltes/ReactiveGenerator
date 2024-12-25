using Xunit;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace ReactiveGenerator.Tests
{
    public class UseBackingFieldsConfigurationTests
    {
        [Fact]
        public Task UseBackingFields_NewFormat()
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
        public Task UseBackingFields_LegacyFormat()
        {
            var source = @"
                [Reactive]
                public partial class TestClass
                {
                    public partial string Name { get; set; }
                }";

            var analyzerConfigOptions = new Dictionary<string, string> 
            { 
                ["UseBackingFields"] = "true" 
            };

            return TestAndVerify(source, analyzerConfigOptions);
        }

        [Fact]
        public Task UseBackingFields_BothFormats_NewTakesPrecedence()
        {
            var source = @"
                [Reactive]
                public partial class TestClass
                {
                    public partial string Name { get; set; }
                }";

            var analyzerConfigOptions = new Dictionary<string, string> 
            { 
                ["build_property.UseBackingFields"] = "true",
                ["UseBackingFields"] = "false"
            };

            return TestAndVerify(source, analyzerConfigOptions);
        }

        [Fact]
        public Task UseBackingFields_BothFormats_BothTrue()
        {
            var source = @"
                [Reactive]
                public partial class TestClass
                {
                    public partial string Name { get; set; }
                }";

            var analyzerConfigOptions = new Dictionary<string, string> 
            { 
                ["build_property.UseBackingFields"] = "true",
                ["UseBackingFields"] = "true"
            };

            return TestAndVerify(source, analyzerConfigOptions);
        }

        [Fact]
        public Task UseBackingFields_InvalidValue_DefaultsToFalse()
        {
            var source = @"
                [Reactive]
                public partial class TestClass
                {
                    public partial string Name { get; set; }
                }";

            var analyzerConfigOptions = new Dictionary<string, string> 
            { 
                ["build_property.UseBackingFields"] = "invalid_value"
            };

            return TestAndVerify(source, analyzerConfigOptions);
        }

        [Fact]
        public Task UseBackingFields_EmptyValue_DefaultsToFalse()
        {
            var source = @"
                [Reactive]
                public partial class TestClass
                {
                    public partial string Name { get; set; }
                }";

            var analyzerConfigOptions = new Dictionary<string, string> 
            { 
                ["build_property.UseBackingFields"] = ""
            };

            return TestAndVerify(source, analyzerConfigOptions);
        }

        [Fact]
        public Task UseBackingFields_NoConfig_DefaultsToFalse()
        {
            var source = @"
                [Reactive]
                public partial class TestClass
                {
                    public partial string Name { get; set; }
                }";

            return TestAndVerify(source, null);
        }

        [Fact]
        public Task UseBackingFields_CrossAssembly_NewFormat()
        {
            var externalAssemblySource = @"
                [Reactive]
                public partial class ExternalClass
                {
                    public partial string ExternalProp { get; set; }
                }";

            var mainAssemblySource = @"
                public partial class DerivedClass : ExternalClass
                {
                    [Reactive]
                    public partial string LocalProp { get; set; }
                }";

            var analyzerConfigOptions = new Dictionary<string, string> 
            { 
                ["build_property.UseBackingFields"] = "true"
            };

            return TestCrossAssemblyAndVerify(externalAssemblySource, mainAssemblySource, analyzerConfigOptions);
        }

        [Fact]
        public Task UseBackingFields_CrossAssembly_LegacyFormat()
        {
            var externalAssemblySource = @"
                [Reactive]
                public partial class ExternalClass
                {
                    public partial string ExternalProp { get; set; }
                }";

            var mainAssemblySource = @"
                public partial class DerivedClass : ExternalClass
                {
                    [Reactive]
                    public partial string LocalProp { get; set; }
                }";

            var analyzerConfigOptions = new Dictionary<string, string> 
            { 
                ["UseBackingFields"] = "true"
            };

            return TestCrossAssemblyAndVerify(externalAssemblySource, mainAssemblySource, analyzerConfigOptions);
        }

        [Fact]
        public Task UseBackingFields_WithComplexProperties()
        {
            var source = @"
                [Reactive]
                public partial class TestClass
                {
                    public partial string Name { get; set; }
                    protected partial int Age { get; private set; }
                    internal partial System.Collections.Generic.List<string> Items { get; set; }
                }";

            var analyzerConfigOptions = new Dictionary<string, string> 
            { 
                ["build_property.UseBackingFields"] = "true"
            };

            return TestAndVerify(source, analyzerConfigOptions);
        }

        [Fact]
        public Task UseBackingFields_WithInheritance()
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

            var analyzerConfigOptions = new Dictionary<string, string> 
            { 
                ["build_property.UseBackingFields"] = "true"
            };

            return TestAndVerify(source, analyzerConfigOptions);
        }

        [Fact]
        public Task UseBackingFields_WithReactiveObjectInheritance()
        {
            var source = @"
                using ReactiveUI;
                
                [Reactive]
                public partial class TestClass : ReactiveObject
                {
                    public partial string Name { get; set; }
                    public partial int Age { get; set; }
                }";

            var analyzerConfigOptions = new Dictionary<string, string> 
            { 
                ["build_property.UseBackingFields"] = "true"
            };

            return TestAndVerify(source, analyzerConfigOptions);
        }

        [Fact]
        public Task UseBackingFields_WithGenericClass()
        {
            var source = @"
                [Reactive]
                public partial class TestClass<T> where T : class
                {
                    public partial T? Value { get; set; }
                    public partial System.Collections.Generic.List<T> Items { get; set; }
                }";

            var analyzerConfigOptions = new Dictionary<string, string> 
            { 
                ["build_property.UseBackingFields"] = "true"
            };

            return TestAndVerify(source, analyzerConfigOptions);
        }

        [Fact]
        public Task UseBackingFields_WithNestedClasses()
        {
            var source = @"
                [Reactive]
                public partial class OuterClass
                {
                    public partial string OuterProp { get; set; }

                    [Reactive]
                    public partial class InnerClass
                    {
                        public partial string InnerProp { get; set; }
                    }
                }";

            var analyzerConfigOptions = new Dictionary<string, string> 
            { 
                ["build_property.UseBackingFields"] = "true"
            };

            return TestAndVerify(source, analyzerConfigOptions);
        }

        [Fact]
        public Task UseBackingFields_WithOverrideProperties()
        {
            var source = @"
                [Reactive]
                public partial class BaseClass
                {
                    public virtual partial string VirtualProp { get; set; }
                }

                [Reactive]
                public partial class DerivedClass : BaseClass
                {
                    public override partial string VirtualProp { get; set; }
                }";

            var analyzerConfigOptions = new Dictionary<string, string> 
            { 
                ["build_property.UseBackingFields"] = "true"
            };

            return TestAndVerify(source, analyzerConfigOptions);
        }

        private Task TestAndVerify(string source, Dictionary<string, string>? analyzerConfigOptions = null)
        {
            return SourceGeneratorTestHelper.TestAndVerify(
                source,
                analyzerConfigOptions,
                generators: new ReactiveGenerator());
        }

        private Task TestCrossAssemblyAndVerify(string externalSource, string mainSource, Dictionary<string, string>? analyzerConfigOptions = null)
        {
            return SourceGeneratorTestHelper.TestCrossAssemblyAndVerifyWithExternalGen(
                externalSource,
                mainSource,
                analyzerConfigOptions,
                new ReactiveGenerator());
        }
    }
}
