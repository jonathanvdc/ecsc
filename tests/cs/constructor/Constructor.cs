using System;

public struct Vector2
{
    public Vector2(int x, int y)
    {
        this.x = x;
        this.y = y;
    }

    public int x, y;
}

public class Base
{
    public Base()
    {

    }

    public Base(int x)
    {
        this.x = x;
    }

    public int x;
}

public class Derived : Base
{
    public Derived()
        : base()
    {

    }

    public Derived(string x)
        : this()
    {

    }

    public Derived(int x)
    {

    }

    public Derived(ref int x)
        : base(x)
    {

    }

    public Derived(double x)
        : this((int)x)
    {

    }
}

public class Program
{
    public static void Main()
    {
        var b = new Base(4);
        Console.WriteLine(new Derived().x);
        Console.WriteLine(new Derived(3).x);
        Console.WriteLine(new Derived(ref b.x).x);
        Console.WriteLine(new Derived() { x = 2 }.x);
    }
}
