using System;

public class Herp
{
    public Herp()
    {
        w = 10;
    }

    public static readonly int w;
}

public static class Program
{
    public static void Main()
    {
        var herp = new Herp();
        Console.WriteLine(Herp.w);
    }
}
