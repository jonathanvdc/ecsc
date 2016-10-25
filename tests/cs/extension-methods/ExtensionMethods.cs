using System;

public class Herp
{
    public Herp(int X)
    {
        this.X = X;
    }

    public int X { get; private set; }
}

public static class HerpExtensions
{
    public static void PrintX(this Herp Value)
    {
        Console.WriteLine(Value.X);
    }
}

public static class Program
{
    public static void Main()
    {
        var herp = new Herp(20);
        herp.PrintX();
    }
}
