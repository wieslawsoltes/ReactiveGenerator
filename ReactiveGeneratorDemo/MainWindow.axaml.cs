using Avalonia.Controls;
using ReactiveGeneratorDemo.ViewModels;

namespace ReactiveGeneratorDemo;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var test = new Test
        {
            Person = new Person { FirstName = "John", LastName = "Doe", Age = 30 },
            Car = new Car { Make = "Toyota" }
        };
            
        DataContext = test;
    }
}
