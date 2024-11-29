using System;
using System.Reactive.Linq;
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
            .When.FirstName
            .Subscribe(name => Console.WriteLine($"Name changed to: {name}"));

        person
            .When.FirstName
            .CombineLatest(person.When.LastName)
            .Subscribe(tuple =>
            {
                var (firstName, lastName) = tuple;
                Console.WriteLine($"{firstName} {lastName}");
            });

        var test = new Test
        {
            Person = person,
            Car = new Car { Make = "Toyota" },
            OaphViewModel = new OaphViewModel()
        };

        DataContext = test;
    }
}
