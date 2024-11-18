using Avalonia.Controls;

namespace InpcDemo;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var person = new Person { FirstName = "John", LastName = "Doe", Age = 30 };

        DataContext = person;
    }
}
