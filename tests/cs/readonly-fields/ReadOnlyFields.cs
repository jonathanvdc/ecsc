using System;

public struct Herp
{
    public Herp(int y)
    {
        this.x = 10;
        this.y = y;
    }

    static Herp()
    {
        w = 40;
    }

    public readonly int x;
    public readonly int y;
    public static readonly int z = 30;
    public static readonly int w;
}

public class Derp
{
    public Derp(int y)
    {
        this.y = y;
    }

    static Derp()
    {
        w = 40;
    }

    public readonly int x = 10;
    public readonly int y;
    public static readonly int z = 30;
    public static readonly int w;
}

public static class Program
{
    public static void Main()
    {
        var herp = new Herp(20);
        Console.WriteLine(herp.x);
        Console.WriteLine(herp.y);
        Console.WriteLine(Herp.z);
        Console.WriteLine(Herp.w);
        var derp = new Derp(20);
        Console.WriteLine(derp.x);
        Console.WriteLine(derp.y);
        Console.WriteLine(Derp.z);
        Console.WriteLine(Derp.w);
    }
}
