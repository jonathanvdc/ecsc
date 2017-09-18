using System;

public delegate T2 Map<T1, T2>(T1 x);

public static class Program
{
    private static int Apply(Map<int, int> f, int x)
    {
        return f(x);
    }

    private static int Square(int x)
    {
        return x * x;
    }

    public static void Main()
    {
        Console.WriteLine(Apply(Square, 10));
    }
}