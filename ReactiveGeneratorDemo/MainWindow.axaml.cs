using System;
using Avalonia.Controls;
using ReactiveGeneratorDemo.ViewModels;

namespace ReactiveGeneratorDemo;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var person = new Person { FirstName = "John", LastName = "Doe", Age = 30 };

        person
            .WhenAnyFirstName()
            .Subscribe(name => Console.WriteLine($"Name changed to: {name}"));

        person.FirstName = "Jane";

        var test = new Test
        {
            Person = person,
            Car = new Car { Make = "Toyota" }
        };
            
        DataContext = test;
    }
}
