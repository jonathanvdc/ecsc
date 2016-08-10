using System;

public class Customer
{
    public Customer() { }

    public string First { get; set; } = "Jane";
    public string Last { get; set; } = "Doe";
}

public class ImmutableCustomer
{
    public ImmutableCustomer() { }

    public string First { get; } = "Jane";
    public string Last { get; } = "Doe";
}

public static class Program
{
    public static void Main(string[] Args)
    {
        var c1 = new ImmutableCustomer();
        Console.WriteLine(c1.First + " " + c1.Last);
        var c2 = new Customer();
        Console.WriteLine(c2.First + " " + c2.Last);
        c2.First = "John";
        c2.Last = "Smith";
        Console.WriteLine(c2.First + " " + c2.Last);
    }
}
