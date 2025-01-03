namespace ReactiveGenerator.Tests;

public class ReactiveGeneratorTests
{
    private Task TestAndVerify(string source, Dictionary<string, string>? analyzerConfigOptions = null)
    {
        return SourceGeneratorTestHelper.TestAndVerify(
            source,
            analyzerConfigOptions,
            generators: new ReactiveGenerator());
    }

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

        var analyzerConfigOptions = new Dictionary<string, string> { ["build_property.UseBackingFields"] = "true" };

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

    [Fact]
    public Task PropertyLevelReactiveWithMixedProperties()
    {
        var source = @"
            public partial class TestClass
            {
                [Reactive]
                public partial string ReactiveProp { get; set; }
                
                public partial string NonReactiveProp { get; set; }
                
                [Reactive]
                private partial int PrivateReactiveProp { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task PropertyLevelReactiveWithInheritance()
    {
        var source = @"
            public partial class BaseClass
            {
                [Reactive]
                public partial string BaseProp { get; set; }
            }

            public partial class DerivedClass : BaseClass
            {
                [Reactive]
                public partial string DerivedProp { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task PropertyLevelReactiveWithGenericType()
    {
        var source = @"
            public partial class TestClass<T>
            {
                [Reactive]
                public partial T TypedProp { get; set; }

                [Reactive]
                public partial List<T> TypedListProp { get; set; }

                [Reactive]
                public partial Dictionary<string, T> TypedDictProp { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task PropertyLevelReactiveWithCustomAccessors()
    {
        var source = @"
            public partial class TestClass
            {
                [Reactive]
                public partial string PropWithPrivateSet { get; private set; }

                [Reactive]
                protected partial string PropWithProtectedGet { private get; set; }

                [Reactive]
                internal partial string PropWithInternalAccessors { internal get; internal set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ReactiveObjectDerivedWithObservableProperties()
    {
        var source = @"
            using ReactiveUI;
            
            public partial class TestViewModel : ReactiveObject
            {
                [Reactive]
                public partial string Name { get; set; }

                [Reactive]
                public partial int Age { get; set; }

                [ObservableAsProperty]
                public partial string FullName { get; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ReactiveObjectDerivedWithComplexProperties()
    {
        var source = @"
            using ReactiveUI;
            using System.Collections.ObjectModel;
            
            public partial class TestViewModel : ReactiveObject
            {
                [Reactive]
                public partial ObservableCollection<string> Items { get; set; }

                [Reactive]
                public partial Dictionary<string, List<int>> ComplexData { get; set; }

                [Reactive]
                public partial (string Name, int Count) TupleData { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ReactiveObjectDerivedWithInheritance()
    {
        var source = @"
            using ReactiveUI;
            
            public partial class BaseViewModel : ReactiveObject
            {
                [Reactive]
                public partial string BaseProp { get; set; }
            }

            public partial class DerivedViewModel : BaseViewModel
            {
                [Reactive]
                public partial string DerivedProp { get; set; }
            }

            public partial class GrandChildViewModel : DerivedViewModel
            {
                [Reactive]
                public partial string GrandChildProp { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ReactiveObjectDerivedWithInitProperties()
    {
        var source = @"
            using ReactiveUI;
            
            public partial class TestViewModel : ReactiveObject
            {
                [Reactive]
                public partial string ReadWriteProp { get; set; }

                [Reactive]
                public partial string InitOnlyProp { get; init; }

                [Reactive]
                public partial string GetOnlyProp { get; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ReactiveObjectDerivedWithCustomImplementations()
    {
        var source = @"
            using ReactiveUI;
            
            public partial class TestViewModel : ReactiveObject
            {
                private string _customProp;
                
                [Reactive]
                public partial string CustomProp 
                { 
                    get => _customProp;
                    set
                    {
                        _customProp = value?.ToUpper() ?? string.Empty;
                        this.RaisePropertyChanged(nameof(CustomProp));
                    }
                }

                [Reactive]
                public partial string RegularProp { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task NestedPrivateClassWithReactiveProperties()
    {
        var source = @"
            public partial class OuterClass
            {
                [Reactive]
                private partial class PrivateInnerClass
                {
                    public partial string InnerProp { get; set; }
                    private partial int PrivateInnerProp { get; set; }
                }

                [Reactive]
                protected partial class ProtectedInnerClass
                {
                    public partial string ProtectedClassProp { get; set; }
                }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task MultiLevelNestedClassesWithReactiveProperties()
    {
        var source = @"
            public partial class Level1
            {
                [Reactive]
                public partial class Level2
                {
                    public partial string Level2Prop { get; set; }

                    [Reactive]
                    private partial class Level3
                    {
                        public partial string Level3Prop { get; set; }
                        
                        [Reactive]
                        internal partial class Level4
                        {
                            public partial string Level4Prop { get; set; }
                        }
                    }
                }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task NestedClassesWithDifferentAccessModifiers()
    {
        var source = @"
            [Reactive]
            public partial class Container
            {
                public partial string ContainerProp { get; set; }

                [Reactive]
                private partial class Private
                {
                    public partial string PrivateProp { get; set; }
                }

                [Reactive]
                protected partial class Protected
                {
                    public partial string ProtectedProp { get; set; }
                }

                [Reactive]
                internal partial class Internal
                {
                    public partial string InternalProp { get; set; }
                }

                [Reactive]
                public partial class Public
                {
                    public partial string PublicProp { get; set; }
                }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task NestedClassesWithGenericConstraints()
    {
        var source = @"
            public partial class Container<T>
                where T : class
            {
                [Reactive]
                public partial class Nested<U>
                    where U : struct
                {
                    public partial T? RefProp { get; set; }
                    public partial U ValueProp { get; set; }
                    public partial Dictionary<T, List<U>> ComplexProp { get; set; }
                }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task NestedClassesWithInheritanceAndInterfaces()
    {
        var source = @"
            public interface INestedInterface
            {
                string Name { get; set; }
            }

            public partial class Container
            {
                [Reactive]
                public abstract partial class NestedBase
                {
                    public abstract string AbstractProp { get; set; }
                    public virtual partial string VirtualProp { get; set; }
                }

                [Reactive]
                public partial class NestedDerived : NestedBase, INestedInterface
                {
                    public override string AbstractProp { get; set; }
                    public override partial string VirtualProp { get; set; }
                    string INestedInterface.Name { get; set; }
                    public partial string RegularProp { get; set; }
                }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task NestedClassesWithReactiveObjectInheritance()
    {
        var source = @"
            using ReactiveUI;
            
            public partial class Container
            {
                [Reactive]
                public partial class NestedReactiveViewModel : ReactiveObject
                {
                    public partial string ViewModelProp { get; set; }
                    
                    [Reactive]
                    private partial class InnerViewModel : ReactiveObject
                    {
                        public partial string InnerProp { get; set; }
                    }
                }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task NestedClassesWithStaticAndInstanceMembers()
    {
        var source = @"
            public partial class Container
            {
                [Reactive]
                public partial class Nested
                {
                    public static partial string StaticProp { get; set; }
                    public partial string InstanceProp { get; set; }
                    
                    [Reactive]
                    private static partial class StaticNested
                    {
                        public static partial string StaticNestedProp { get; set; }
                    }
                }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task NestedClassesWithCustomPropertyImplementation()
    {
        var source = @"
            public partial class Container
            {
                [Reactive]
                public partial class Nested
                {
                    private string _customImpl = string.Empty;
                    
                    [Reactive]
                    public partial string CustomImpl 
                    { 
                        get => _customImpl;
                        set => _customImpl = value?.ToUpper() ?? string.Empty;
                    }
                    
                    public partial string RegularProp { get; set; }
                }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task NestedClassesWithMixedReactiveScopes()
    {
        var source = @"
            [Reactive]
            public partial class Container
            {
                public partial string ContainerProp { get; set; }

                public partial class NonReactiveNested
                {
                    [Reactive]
                    public partial string PropertyLevelReactiveProp { get; set; }
                    public partial string NonReactiveProp { get; set; }
                }

                [Reactive]
                public partial class ReactiveNested
                {
                    public partial string AllPropsReactiveProp { get; set; }
                    [IgnoreReactive]
                    public partial string IgnoredProp { get; set; }
                }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task NestedClassesWithInitOnlyProperties()
    {
        var source = @"
            public partial class Container
            {
                [Reactive]
                public partial class Nested
                {
                    public partial string RegularProp { get; set; }
                    public partial string InitOnlyProp { get; init; }
                    
                    [Reactive]
                    private partial class InnerNested
                    {
                        public partial string InnerRegularProp { get; set; }
                        public partial string InnerInitOnlyProp { get; init; }
                    }
                }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task GenericPropertyImplementation()
    {
        var source = @"
            [Reactive]
            internal partial class GenericViewModel<T>
            {
                public partial T Value { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task GenericPropertyWithNullableType()
    {
        var source = @"
            [Reactive]
            internal partial class GenericViewModel<T>
                where T : class
            {
                public partial T? Value { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task GenericPropertyWithConstraints()
    {
        var source = @"
            [Reactive]
            internal partial class GenericViewModel<T>
                where T : class, new()
            {
                public partial T Value { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task GenericPropertyWithMultipleTypeParameters()
    {
        var source = @"
            [Reactive]
            internal partial class GenericViewModel<T, U>
                where T : class
                where U : struct
            {
                public partial T? RefValue { get; set; }
                public partial U ValueType { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task GenericPropertyWithNestedTypes()
    {
        var source = @"
            [Reactive]
            internal partial class GenericViewModel<T>
            {
                public partial List<T> ListValue { get; set; }
                public partial Dictionary<string, T> DictValue { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task GenericPropertyWithReactiveObjectBase()
    {
        var source = @"
            using ReactiveUI;

            [Reactive]
            internal partial class GenericViewModel<T> : ReactiveObject
                where T : class
            {
                public partial T? Value { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task GenericPropertyWithCustomAccessibility()
    {
        var source = @"
            [Reactive]
            internal partial class GenericViewModel<T>
            {
                public partial T Value { get; private set; }
                protected partial T ProtectedValue { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task GenericPropertyWithReadOnlyAccess()
    {
        var source = @"
            [Reactive]
            internal partial class GenericViewModel<T>
            {
                public partial T Value { get; }
                public partial T ReadWriteValue { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task GenericPropertyWithInitSetter()
    {
        var source = @"
            [Reactive]
            internal partial class GenericViewModel<T>
            {
                public partial T InitValue { get; init; }
                public partial T RegularValue { get; set; }
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
        }

        [Reactive]
        public partial class ReactiveDerivedViewModel : NonReactiveViewModel
        {
            public partial string ReactiveProp { get; set; }
        }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ReactiveObjectWithSingleReactiveProperty()
    {
        var source = @"
        using ReactiveUI;
        
        public partial class Car : ReactiveObject
        {
            [Reactive]
            public partial string? Make { get; set; }

            public partial string Model { get; set; }  // Should not be processed
        }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ReactiveObjectWithClassLevelReactive()
    {
        var source = @"
        using ReactiveUI;
        
        [Reactive]
        internal partial class ReactiveViewModel : ReactiveObject
        {
            public partial string FirstName { get; set; }
            public partial string LastName { get; set; }
        }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ReactiveObjectWithoutAnyReactiveAttribute()
    {
        var source = @"
        using ReactiveUI;
        
        public partial class OaphViewModel<T> : ReactiveObject
        {
            public partial string ComputedValue { get; }  // Should not be processed
            public partial string NormalProperty { get; set; }  // Should not be processed
        }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ReactiveObjectWithNullableGenerics()
    {
        var source = @"
        using System;
        using ReactiveUI;

        public partial class NullableGenericsTest<T>
        {
            [Reactive]
            public partial DateTime? StartDate { get; set; }

            [Reactive]
            public partial DateTime? EndDate { get; set; }
        }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ReactiveObjectWithMixedReactiveProperties()
    {
        var source = @"
        using ReactiveUI;
        
        public partial class MixedViewModel : ReactiveObject
        {
            [Reactive]
            public partial string ReactiveProperty { get; set; }
            
            public partial string NonReactiveProperty { get; set; }
            
            [Reactive]
            public partial int? NullableReactiveProperty { get; set; }
            
            [ObservableAsProperty]
            public partial string ComputedProperty { get; }
        }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ReactiveObjectWithClassLevelReactiveAndIgnoreOverride()
    {
        var source = @"
        using ReactiveUI;
        
        [Reactive]
        public partial class AllReactiveViewModel : ReactiveObject
        {
            public partial string Property1 { get; set; }
            
            [IgnoreReactive]
            public partial string IgnoredProperty { get; set; }
            
            public partial string Property2 { get; set; }
        }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ReactiveObjectWithPropertyInitializer()
    {
        var source = @"
        using ReactiveUI;
        
        [Reactive]
        public partial class InitializedViewModel : ReactiveObject
        {
            public partial string Name { get; set; } = string.Empty;
            public partial int Count { get; set; } = 42;
        }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task NonReactiveObjectWithMixedProperties()
    {
        var source = @"
        public partial class RegularClass
        {
            [Reactive]
            public partial string ReactiveProperty { get; set; }
            
            public partial string NonReactiveProperty { get; set; }
        }";

        return TestAndVerify(source);
    }
    
    [Fact]
    public Task RequiredPropertyTest()
    {
        var source = @"
        [Reactive]
        public partial class Person
        {
            public required partial string Name { get; set; }
            public required partial int Age { get; set; }
            public partial string? OptionalNickname { get; set; }
        }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task RequiredPropertyWithReactiveObjectTest()
    {
        var source = @"
        using ReactiveUI;
        
        [Reactive]
        public partial class Person : ReactiveObject
        {
            public required partial string Name { get; set; }
            public required partial DateOnly BirthDate { get; set; }
        }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task RequiredPropertyWithInheritanceTest()
    {
        var source = @"
        [Reactive]
        public abstract partial class Entity
        {
            public required partial string Id { get; set; }
        }

        [Reactive]
        public partial class Person : Entity
        {
            public required partial string Name { get; set; }
        }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task RequiredPropertyWithMixedModifiersTest()
    {
        var source = @"
        [Reactive]
        public partial class BaseClass
        {
            public required virtual partial string VirtualRequired { get; set; }
        }

        [Reactive]
        public partial class DerivedClass : BaseClass
        {
            public required override partial string VirtualRequired { get; set; }
            public required partial string LocalRequired { get; set; }
        }";

        return TestAndVerify(source);
    }
    
    [Fact]
    public Task AllPropertyModifiersTest()
    {
        var source = @"
            [Reactive]
            public partial class Base
            {
                // Static properties
                public static partial string StaticProp { get; set; }
                protected static partial string ProtectedStaticProp { get; set; }

                // Virtual and abstract properties
                public virtual partial string VirtualProp { get; set; }
                public abstract partial string AbstractProp { get; set; }

                // Required properties
                public required partial string RequiredProp { get; set; }

                // Mixed modifiers
                public required virtual partial string RequiredVirtualProp { get; set; }
            }

            [Reactive]
            public partial class Derived : Base
            {
                // New properties (shadowing)
                public new partial string VirtualProp { get; set; }

                // Override properties
                public override partial string AbstractProp { get; set; }

                // Sealed override properties
                public sealed override partial string RequiredVirtualProp { get; set; }

                // Static shadows
                public new static partial string StaticProp { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task StaticPropertyTest()
    {
        var source = @"
            [Reactive]
            public partial class TestClass
            {
                public static partial string GlobalConfig { get; set; }
                private static partial int Counter { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task NewModifierTest()
    {
        var source = @"
            [Reactive]
            public partial class Base
            {
                public partial string Name { get; set; }
            }

            [Reactive]
            public partial class Derived : Base
            {
                public new partial string Name { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task SealedOverrideTest()
    {
        var source = @"
            [Reactive]
            public partial class Base
            {
                public virtual partial string VirtualProp { get; set; }
            }

            [Reactive]
            public partial class Derived : Base
            {
                public sealed override partial string VirtualProp { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task MixedModifiersTest()
    {
        var source = @"
            [Reactive]
            public partial class TestClass
            {
                // Static + Required
                public static required partial string StaticRequired { get; set; }

                // New + Static
                public new static partial string NewStatic { get; set; }

                // Virtual + Required
                public virtual required partial string VirtualRequired { get; set; }

                // Override + Sealed
                public sealed override partial string SealedOverride { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task AccessorModifiersTest()
    {
        var source = @"
            [Reactive]
            public partial class TestClass
            {
                // Property with private setter
                public partial string Name { get; private set; }

                // Protected property with private getter
                protected partial string Id { private get; set; }

                // Internal property with protected setter
                internal partial string Code { get; protected set; }

                // Public property with protected internal setter
                public partial string Key { get; protected internal set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task ModifierOrderingTest()
    {
        var source = @"
            [Reactive]
            public partial class Base
            {
                // Base class properties for override/shadowing
                public virtual partial string VirtualProp { get; set; }
                protected virtual partial string ProtectedVirtualProp { get; set; }
                public static partial string StaticProp { get; set; }
                public abstract partial string AbstractProp { get; set; }
            }

            [Reactive]
            public partial class Derived : Base
            {
                // Test new modifier comes first
                public new partial string VirtualProp { get; set; }
                
                // Test new + static ordering
                public new static partial string StaticProp { get; set; }
                
                // Test override + sealed + required ordering
                public sealed override required partial string ProtectedVirtualProp { get; set; }
                
                // Test override + required ordering
                public override required partial string AbstractProp { get; set; }
                
                // Test required only
                public required partial string RequiredProp { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task InheritanceModifiersTest()
    {
        var source = @"
            [Reactive]
            public abstract partial class BaseClass
            {
                // Virtual property
                public virtual partial string VirtualProp { get; set; }
                
                // Abstract property
                public abstract partial string AbstractProp { get; set; }
            }

            [Reactive]
            public partial class DerivedClass : BaseClass
            {
                // Override property
                public override partial string VirtualProp { get; set; }
                
                // Sealed override property
                public sealed override partial string AbstractProp { get; set; }
            }";

        return TestAndVerify(source);
    }

    [Fact]
    public Task RequiredModifierTest()
    {
        var source = @"
            [Reactive]
            public partial class TestClass
            {
                // Required property
                public required partial string Name { get; set; }
                
                // Required with virtual
                public virtual required partial string VirtualRequired { get; set; }
                
                // Required with different access levels
                protected required partial string ProtectedRequired { get; set; }
                private required partial string PrivateRequired { get; set; }
            }";

        return TestAndVerify(source);
    }
}
