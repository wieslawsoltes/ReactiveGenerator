using System.Collections.Generic;

namespace InpcDemo;

public partial class Person
{
    [Reactive]
    public partial string FirstName { get; set; }

    [Reactive]
    public partial string LastName { get; set; }

    [Reactive]
    public partial int Age { get; set; }
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
