using System;

public static class Program
{
    public static int f(int x, int y)
    {
        x = y;
        x += y;
        return x;
    }

    public static void Main()
    {
        Console.WriteLine(Program.f(1, 3));
    }
}
