using System;

public abstract class Base
{
    public Base() { }

    public abstract int f();
    public virtual int g()
    {
        return 2;
    }

    public abstract int x { get; }
}

public class Derived : Base
{
    public Derived() { }

    public sealed override int f()
    {
        return 3;
    }

    public new int g()
    {
        return 3;
    }

    public override int x { get { return 4; } }
}

public static class Program
{
    public static void Main()
    {
        var deriv = new Derived();
        Console.WriteLine(deriv.f());
        Console.WriteLine(((Base)deriv).f());
        Console.WriteLine(deriv.g());
        Console.WriteLine(((Base)deriv).g());
        Console.WriteLine(deriv.x);
        Console.WriteLine(((Base)deriv).x);
    }
}
