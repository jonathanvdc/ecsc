using System;

public enum Colors
{
    Red = 1,
    Alpha,
    Green = 13,
    Blue
}

public static class Program
{
    public static void Main(string[] Args)
    {
        Console.WriteLine((int)Colors.Red);
        Console.WriteLine((int)Colors.Alpha);
        Console.WriteLine((int)Colors.Green);
        Console.WriteLine((int)Colors.Blue);
    }
}
