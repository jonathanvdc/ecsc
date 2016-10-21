using System;

public class Herp
{
    public Herp()
    {

    }

    public readonly int x;
}

public static class Program
{
    public static void Main()
    {
        var herp = new Herp();
        herp.x = 10;
        Console.WriteLine(herp.x);
    }
}
