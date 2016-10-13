using System;

public static class Program
{
    static Program()
    {
        X = 10;
    }

    public static int X { get; private set; }
    public static int Y { get; } = 20;

    public static void Main(string[] Args)
    {
        Console.WriteLine(X);
        Console.WriteLine(Y);
    }
}
