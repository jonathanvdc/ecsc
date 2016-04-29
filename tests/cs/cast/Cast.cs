using System;

public class A
{
    public A()
    { }
}

public class B : A
{
    public B()
    { }
}

public static class Program
{
    public static void Main()
    {
        B x = default(B);
        Console.WriteLine(x is A);
        Console.WriteLine(x is B);
        Console.WriteLine(x as A);
        Console.WriteLine(x as B);
    }
}
