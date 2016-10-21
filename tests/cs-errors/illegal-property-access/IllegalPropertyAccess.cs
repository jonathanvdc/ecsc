using System;

public class Herp
{
    public Herp()
    { }

    public Herp(int X)
    {
        this.X = X;
    }

    public int X { get; private set; } = 0;
}

public static class Program
{
    public static void Main()
    {
        var herp = new Herp(20);
        herp.X = 10;
        Console.WriteLine(herp.X);
    }
}
