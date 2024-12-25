using Xunit;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace ReactiveGenerator.Tests
{
    public class CrossAssemblyTests
    {
        /// <summary>
        /// 1) External assembly: [Reactive] base class physically gets INPC from the generator
        ///    Main assembly: derived class sees that the base already has INPC => no second partial generated.
        /// </summary>
        [Fact]
        public Task ExternalBaseHasReactiveClass_ThenNoDoubleINPCInDerived()
        {
            var externalAssemblySource = @"
using System;

namespace ExternalLib
{
    [Reactive]
    public partial class ExternalBase
    {
        public partial string ExternalProp { get; set; }
    }
}
";
            var mainAssemblySource = @"
using ExternalLib;

namespace MainLib
{
    // Derived class also has [Reactive] property
    // but won't generate a second INPC partial if the base physically has it.
    public partial class DerivedClass : ExternalBase
    {
        [Reactive]
        public partial int LocalProp { get; set; }
    }
}
";

            return SourceGeneratorTestHelper.TestCrossAssemblyAndVerifyWithExternalGen(
                externalAssemblySource,
                mainAssemblySource,
                // optional config:
                // new Dictionary<string, string> { ["build_property.UseBackingFields"] = "true" },
                null,
                new ReactiveGenerator()
            );
        }

        /// <summary>
        /// 2) External assembly: base class has [Reactive], plus [IgnoreReactive] on some property.
        ///    Main assembly: derived class => we confirm no double INPC, and ignored property remains ignored.
        /// </summary>
        [Fact]
        public Task ExternalBaseHasIgnoreReactive_ThenDerivedIgnoresItToo()
        {
            var externalAssemblySource = @"
using System;

namespace ExternalLib
{
    [Reactive]
    public partial class BaseWithIgnore
    {
        [IgnoreReactive]
        public partial string IgnoredProp { get; set; }

        public partial string NormalProp { get; set; }
    }
}
";
            var mainAssemblySource = @"
using ExternalLib;

namespace MainLib
{
    public partial class DerivedIgnore : BaseWithIgnore
    {
        // This is local partial. We'll see no duplication of INPC, 
        // and 'IgnoredProp' won't generate in external or derived code.
        [Reactive]
        public partial string ExtraProp { get; set; }
    }
}
";

            return SourceGeneratorTestHelper.TestCrossAssemblyAndVerifyWithExternalGen(
                externalAssemblySource,
                mainAssemblySource,
                null, // or new Dictionary<string, string> { ... },
                new ReactiveGenerator()
            );
        }

        [Fact]
        public Task ExternalBaseHasPropertyLevelReactive_ThenNoExtraINPCInDerived()
        {
            var externalAssemblySource = @"
using System;

namespace ExternalLib
{
    public partial class ExternalMixed
    {
        [Reactive]
        public partial string ExternalReactiveProp { get; set; }

        public partial string ExternalNonReactiveProp { get; set; }
    }
}";

            var mainAssemblySource = @"
using ExternalLib;

namespace MainLib
{
    public partial class LocalDerived : ExternalMixed
    {
        [Reactive]
        public partial int LocalReactiveProp { get; set; }
    }
}";
            return SourceGeneratorTestHelper.TestCrossAssemblyAndVerifyWithExternalGen(
                externalAssemblySource,
                mainAssemblySource,
                null,
                new ReactiveGenerator());
        }

        /// <summary>
        /// 4) External assembly: class inherits from ReactiveObject (assuming we have ReactiveUI available).
        ///    So it already physically has INPC => main assembly sees that and won't generate again.
        /// </summary>
        [Fact]
        public Task ExternalBaseDerivedFromReactiveObject_ThenNoDoubleINPCInDerived()
        {
            // For this test to run, you'd typically need a ref to ReactiveUI or a mock ReactiveObject in the test references.
            // If you don't have ReactiveObject, just remove this example.
            var externalAssemblySource = @"
using System;
using ReactiveUI;

namespace ExternalLib
{
    public partial class ExternalViewModel : ReactiveObject
    {
        // Even without [Reactive], ReactiveObject base means we won't generate a new INPC partial.
        public partial string SomeProp { get; set; }
    }
}
";
            var mainAssemblySource = @"
using ExternalLib;

namespace MainLib
{
    public partial class ChildViewModel : ExternalViewModel
    {
        [Reactive]
        public partial int AnotherProp { get; set; }
    }
}
";

            return SourceGeneratorTestHelper.TestCrossAssemblyAndVerifyWithExternalGen(
                externalAssemblySource,
                mainAssemblySource,
                null,
                new ReactiveGenerator()
            );
        }
        
        /// <summary>
        /// External base has [Reactive], derived has no attributes
        /// </summary>
        [Fact]
        public Task BaseHasReactive_DerivedHasNoAttributes()
        {
            var externalAssemblySource = @"
namespace ExternalLib
{
    [Reactive]
    public partial class BaseClass
    {
        public partial string BaseProp { get; set; }
    }
}";

            var mainAssemblySource = @"
using ExternalLib;

namespace MainLib
{
    // No [Reactive] here, should inherit from base
    public partial class DerivedClass : BaseClass
    {
        public partial string DerivedProp { get; set; }
    }
}";

            return SourceGeneratorTestHelper.TestCrossAssemblyAndVerifyWithExternalGen(
                externalAssemblySource,
                mainAssemblySource,
                null,
                new ReactiveGenerator());
        }

        /// <summary>
        /// External base has no attributes, derived has [Reactive]
        /// </summary>
        [Fact]
        public Task BaseHasNoAttributes_DerivedHasReactive()
        {
            var externalAssemblySource = @"
namespace ExternalLib
{
    public partial class BaseClass
    {
        public partial string BaseProp { get; set; }
    }
}";

            var mainAssemblySource = @"
using ExternalLib;

namespace MainLib
{
    [Reactive]
    public partial class DerivedClass : BaseClass
    {
        public partial string DerivedProp { get; set; }
    }
}";

            return SourceGeneratorTestHelper.TestCrossAssemblyAndVerifyWithExternalGen(
                externalAssemblySource,
                mainAssemblySource,
                null,
                new ReactiveGenerator());
        }

        /// <summary>
        /// External base has property-level [Reactive], derived has no attributes
        /// </summary>
        [Fact]
        public Task BaseHasPropertyReactive_DerivedHasNoAttributes()
        {
            var externalAssemblySource = @"
namespace ExternalLib
{
    public partial class BaseClass
    {
        [Reactive]
        public partial string ReactiveProp { get; set; }
        public partial string NonReactiveProp { get; set; }
    }
}";

            var mainAssemblySource = @"
using ExternalLib;

namespace MainLib
{
    public partial class DerivedClass : BaseClass
    {
        public partial string DerivedProp { get; set; }
    }
}";

            return SourceGeneratorTestHelper.TestCrossAssemblyAndVerifyWithExternalGen(
                externalAssemblySource,
                mainAssemblySource,
                null,
                new ReactiveGenerator());
        }

        /// <summary>
        /// External base has no attributes, derived has property-level [Reactive]
        /// </summary>
        [Fact]
        public Task BaseHasNoAttributes_DerivedHasPropertyReactive()
        {
            var externalAssemblySource = @"
namespace ExternalLib
{
    public partial class BaseClass
    {
        public partial string BaseProp { get; set; }
    }
}";

            var mainAssemblySource = @"
using ExternalLib;

namespace MainLib
{
    public partial class DerivedClass : BaseClass
    {
        [Reactive]
        public partial string ReactiveProp { get; set; }
        public partial string NonReactiveProp { get; set; }
    }
}";

            return SourceGeneratorTestHelper.TestCrossAssemblyAndVerifyWithExternalGen(
                externalAssemblySource,
                mainAssemblySource,
                null,
                new ReactiveGenerator());
        }

        /// <summary>
        /// External base has [Reactive], derived has [IgnoreReactive]
        /// </summary>
        [Fact]
        public Task BaseHasReactive_DerivedHasIgnoreReactive()
        {
            var externalAssemblySource = @"
namespace ExternalLib
{
    [Reactive]
    public partial class BaseClass
    {
        public partial string BaseProp { get; set; }
    }
}";

            var mainAssemblySource = @"
using ExternalLib;

namespace MainLib
{
    [IgnoreReactive]
    public partial class DerivedClass : BaseClass
    {
        public partial string DerivedProp { get; set; }
    }
}";

            return SourceGeneratorTestHelper.TestCrossAssemblyAndVerifyWithExternalGen(
                externalAssemblySource,
                mainAssemblySource,
                null,
                new ReactiveGenerator());
        }

        /// <summary>
        /// External base has [Reactive], derived has mixed property-level attributes
        /// </summary>
        [Fact]
        public Task BaseHasReactive_DerivedHasMixedPropertyAttributes()
        {
            var externalAssemblySource = @"
namespace ExternalLib
{
    [Reactive]
    public partial class BaseClass
    {
        public partial string BaseProp { get; set; }
        [IgnoreReactive]
        public partial string IgnoredBaseProp { get; set; }
    }
}";

            var mainAssemblySource = @"
using ExternalLib;

namespace MainLib
{
    public partial class DerivedClass : BaseClass
    {
        // Should be reactive due to base class
        public partial string InheritedReactiveProp { get; set; }
        
        // Should be ignored
        [IgnoreReactive]
        public partial string IgnoredProp { get; set; }
        
        // Should be reactive due to explicit attribute
        [Reactive]
        public partial string ExplicitReactiveProp { get; set; }
    }
}";

            return SourceGeneratorTestHelper.TestCrossAssemblyAndVerifyWithExternalGen(
                externalAssemblySource,
                mainAssemblySource,
                null,
                new ReactiveGenerator());
        }

        /// <summary>
        /// Multiple inheritance levels with mixed reactive attributes
        /// </summary>
        [Fact]
        public Task MultipleInheritanceLevels_MixedReactiveAttributes()
        {
            var externalAssemblySource = @"
namespace ExternalLib
{
    [Reactive]
    public partial class GrandparentClass
    {
        public partial string GrandparentProp { get; set; }
    }

    // No attributes here
    public partial class ParentClass : GrandparentClass
    {
        public partial string ParentProp { get; set; }
        [Reactive]
        public partial string ReactiveParentProp { get; set; }
    }
}";

            var mainAssemblySource = @"
using ExternalLib;

namespace MainLib
{
    // No class-level reactive
    public partial class ChildClass : ParentClass
    {
        public partial string ChildProp { get; set; }
        [Reactive]
        public partial string ReactiveChildProp { get; set; }
    }

    [IgnoreReactive]
    public partial class GrandchildClass : ChildClass
    {
        public partial string GrandchildProp { get; set; }
        [Reactive]
        public partial string ReactiveGrandchildProp { get; set; }
    }
}";

            return SourceGeneratorTestHelper.TestCrossAssemblyAndVerifyWithExternalGen(
                externalAssemblySource,
                mainAssemblySource,
                null,
                new ReactiveGenerator());
        }

        /// <summary>
        /// External base has partial [Reactive] implementation, derived extends it
        /// </summary>
        [Fact]
        public Task BaseHasPartialReactive_DerivedExtends()
        {
            var externalAssemblySource = @"
namespace ExternalLib
{
    [Reactive]
    public abstract partial class BaseClass
    {
        public partial string BaseProp { get; set; }
        public abstract string AbstractProp { get; set; }
        public virtual partial string VirtualProp { get; set; }
    }
}";

            var mainAssemblySource = @"
using ExternalLib;

namespace MainLib
{
    public partial class DerivedClass : BaseClass
    {
        // Implementing abstract prop
        public override string AbstractProp { get; set; }
        
        // Overriding virtual prop
        public override partial string VirtualProp { get; set; }
        
        // New prop
        public partial string DerivedProp { get; set; }
    }
}";

            return SourceGeneratorTestHelper.TestCrossAssemblyAndVerifyWithExternalGen(
                externalAssemblySource,
                mainAssemblySource,
                null,
                new ReactiveGenerator());
        }
    }
}
