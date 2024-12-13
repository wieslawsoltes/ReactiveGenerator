using System.Reactive.Linq;
using ReactiveUI;

namespace ReactiveGenerator.Integration.Tests;

public partial class ObservableAsPropertyHelperIntegrationTests
{
    [Fact]
    public void SimpleViewModel_WhenCounterChanges_UpdatesComputedValue()
    {
        // Arrange
        var viewModel = new SimpleViewModel();
        var values = new List<string>();
        viewModel.WhenAnyValue(x => x.ComputedValue)
            .Subscribe(x => values.Add(x ?? string.Empty));

        // Act
        viewModel.Counter = 1;
        viewModel.Counter = 2;

        // Assert
        Assert.Equal(new[] { "Counter: 0", "Counter: 1", "Counter: 2" }, values);
    }

    [Fact]
    public void GenericViewModel_WhenInputChanges_UpdatesComputedValue()
    {
        // Arrange
        var viewModel = new GenericViewModel<string>();
        var values = new List<string?>();
        viewModel.WhenAnyValue(x => x.ComputedValue)
            .Subscribe(x => values.Add(x));

        // Act
        viewModel.Input = "Hello";
        viewModel.Input = "World";

        // Assert
        Assert.Equal(new[] { null, "Value: Hello", "Value: World" }, values);
    }

    internal partial class SimpleViewModel : ReactiveObject
    {
        [ObservableAsProperty]
        public partial string ComputedValue { get; }
        
        [Reactive]
        public partial int Counter { get; set; }

        public SimpleViewModel()
        {
            ObservableAsPropertyHelper<string> helper = this.WhenAnyCounter()
                .Select(x => $"Counter: {x}")
                .ToProperty(this, x => x.ComputedValue);

            _computedValueHelper = helper;
        }
    }

    internal partial class GenericViewModel<T> : ReactiveObject
        where T : class
    {
        [ObservableAsProperty]
        public partial T? ComputedValue { get; }

        [Reactive]
        public partial T? Input { get; set; }

        public GenericViewModel()
        {
            ObservableAsPropertyHelper<T?> helper = this.WhenAnyInput()
                .Select(x => x != null ? $"Value: {x}" as T : default)
                .ToProperty(this, x => x.ComputedValue);

            _computedValueHelper = helper;
        }
    }
}
