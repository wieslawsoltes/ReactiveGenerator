using System.Reactive.Linq;
using ReactiveUI;

namespace ReactiveGenerator.Integration.Tests;

public partial class ReactiveGeneratorIntegrationTests
{
    [Fact]
    public void NotifyPropertyChanged_SimpleProperty_RaisesEvent()
    {
        // Arrange
        var viewModel = new TestViewModel();
        var propertyChangedRaised = false;
        var propertyName = string.Empty;
            
        viewModel.PropertyChanged += (s, e) => {
            propertyChangedRaised = true;
            propertyName = e.PropertyName;
        };

        // Act
        viewModel.Name = "Test";

        // Assert
        Assert.True(propertyChangedRaised);
        Assert.Equal(nameof(TestViewModel.Name), propertyName);
    }

    [Fact]
    public void WhenAnyValue_SimpleProperty_StreamsValues()
    {
        // Arrange
        var viewModel = new TestViewModel();
        var receivedValues = new List<string>();
        var subscription = viewModel.WhenAnyName().Subscribe(value => receivedValues.Add(value));

        // Act
        viewModel.Name = "First";
        viewModel.Name = "Second";
        viewModel.Name = "Third";

        // Assert
        Assert.Equal(new[] { "", "First", "Second", "Third" }, receivedValues);
        subscription.Dispose();
    }

    [Fact]
    public void ReactiveObject_PropertyChanges_UpdateDependentProperty()
    {
        // Arrange
        var viewModel = new ReactiveViewModel();
        var fullNameChanges = new List<string>();
            
        viewModel.WhenAnyValue(x => x.FullName)
            .Subscribe(value => fullNameChanges.Add(value));

        // Act
        viewModel.FirstName = "John";
        viewModel.LastName = "Doe";

        // Assert
        Assert.Equal(new[] { "", "John ", "John Doe" }, fullNameChanges);
    }

    [Fact]
    public void GenericClass_PropertyChanges_NotifiesCorrectly()
    {
        // Arrange
        var viewModel = new GenericViewModel<string>();
        var receivedValues = new List<string>();
        var subscription = viewModel.WhenAnyValue().Subscribe(value => receivedValues.Add(value));

        // Act
        viewModel.Value = "Test1";
        viewModel.Value = "Test2";

        // Assert
        Assert.Equal(new[] { null, "Test1", "Test2" }, receivedValues);
        subscription.Dispose();
    }

    [Fact]
    public void PropertyLevelReactive_OnlyDecoratedProperties_RaiseEvents()
    {
        // Arrange
        var model = new PropertyLevelModel();
        var reactiveChanges = new List<string>();
        var nonReactiveChanges = new List<string>();
    
        // Skip the initial value
        model.WhenAnyReactiveProp()
            .Skip(1)  // Skip initial value
            .Subscribe(value => reactiveChanges.Add(value));
    
        model.PropertyChanged += (s, e) => {
            if (e.PropertyName == nameof(PropertyLevelModel.NonReactiveProp))
                nonReactiveChanges.Add(model.NonReactiveProp);
        };

        // Act
        model.ReactiveProp = "Test1";
        model.ReactiveProp = "Test1-Updated";
        model.NonReactiveProp = "Test2";

        // Assert
        Assert.Equal(2, reactiveChanges.Count);  // Now we'll only see the two changes
        Assert.Equal("Test1", reactiveChanges[0]);
        Assert.Equal("Test1-Updated", reactiveChanges[1]);
        Assert.Empty(nonReactiveChanges);
    }

    [Fact]
    public void ComplexProperties_CorrectlyTrackChanges()
    {
        // Arrange
        var viewModel = new ComplexViewModel();
        var listChanges = new List<List<string>>();
        var tupleChanges = new List<(string Name, int Age)>();

        viewModel.WhenAnyItems().Subscribe(items => listChanges.Add(new List<string>(items)));
        viewModel.WhenAnyPersonInfo().Subscribe(info => tupleChanges.Add(info));

        // Act
        viewModel.Items = new List<string> { "Item1" };
        viewModel.Items = new List<string> { "Item1", "Item2" };
        viewModel.PersonInfo = ("John", 30);
        viewModel.PersonInfo = ("Jane", 25);

        // Assert
        Assert.Equal(3, listChanges.Count); // Initial + 2 changes
        Assert.Equal(3, tupleChanges.Count); // Initial + 2 changes
        Assert.Equal(("Jane", 25), tupleChanges[2]);
    }

    // Test classes that would be decorated with the generator attributes
    [Reactive]
    internal partial class TestViewModel
    {
        public partial string Name { get; set; } = string.Empty;
    }

    [Reactive]
    internal partial class ReactiveViewModel : ReactiveObject
    {
        public partial string FirstName { get; set; } = string.Empty;
        public partial string LastName { get; set; } = string.Empty;
            
        private string _fullName = string.Empty;
        public string FullName
        {
            get => _fullName;
            private set => this.RaiseAndSetIfChanged(ref _fullName, value);
        }

        public ReactiveViewModel()
        {
            this.WhenAnyValue(x => x.FirstName, x => x.LastName)
                .Subscribe(tuple => 
                {
                    // Always add space after FirstName if it's not empty
                    var firstName = string.IsNullOrEmpty(tuple.Item1) ? "" : tuple.Item1 + " ";
                    FullName = $"{firstName}{tuple.Item2}";
                });
        }
    }

    [Reactive]
    internal partial class GenericViewModel<T>
    {
        public partial T? Value  { get; set; }
    }

    internal partial class PropertyLevelModel
    {
        [Reactive]
        public partial string ReactiveProp { get; set; } = string.Empty;
            
        public string? NonReactiveProp { get; set; }
    }

    [Reactive]
    internal partial class ComplexViewModel
    {
        public ComplexViewModel()
        {
            Items = new List<string>();
            PersonInfo = default;
        }

        public partial List<string> Items { get; set; }
        public partial (string Name, int Age) PersonInfo { get; set; }
    }
}
