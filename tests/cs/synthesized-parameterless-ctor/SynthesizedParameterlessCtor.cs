using System;

public class Vector<T>
{
    public T X;
    public T Y;
}

public static class Program
{
    public static void Print(Vector<int> value)
    {
        Console.WriteLine(value.X);
        Console.WriteLine(value.Y);
    }

    public static void Main()
    {
        var vec = new Vector<int>();
        vec.X = 10;
        vec.Y = 20;
        Print(vec);
    }
}