using System;

public class Herp
{
    public static readonly int w;
}

public static class Program
{
    public static void Main()
    {
        Herp.w = 10;
        Console.WriteLine(Herp.w);
    }
}
