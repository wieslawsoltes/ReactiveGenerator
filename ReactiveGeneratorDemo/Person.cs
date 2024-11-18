using System.Collections.Generic;

namespace ReactiveGeneratorDemo;

public partial class Person
{
    [Reactive]
    public partial string FirstName { get; set; }

    [Reactive]
    public partial string LastName { get; set; }

    [Reactive]
    public partial int Age { get; set; }

    [Reactive] 
    internal partial string Tag { get; private set; }
}

public partial class Student : Person
{
    [Reactive]
    public partial string Address { get; set; }
}

public partial class Teacher : Person
{
    [Reactive]
    public partial List<Student> Students { get; set; }
}
