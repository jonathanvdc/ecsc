using System;

public static class Program
{
    public static int f(ref int x, out int y)
    {
        int z = x;
        y = z;
        return x;
    }

    public static void Main()
    {
        int x = 2, y = 3;
        Console.WriteLine(Program.f(ref x, out y));
        Console.WriteLine(x);
        Console.WriteLine(y);
    }
}
