using System;

public static class Program
{
    public static void PrintInfo<T>(T Value)
    {
        Console.WriteLine(Value.ToString());
        Console.WriteLine(Value.GetType());
        Console.WriteLine(Value);
    }

    public static void Main()
    {
        PrintInfo<int>(0);
    }
}