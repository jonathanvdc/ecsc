using System;

public enum Colors
{
    Red = 1,
    Alpha,
    Green = 13,
    Blue
}

public enum LongColors : long
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
        Console.WriteLine((long)Colors.Blue);
        Console.WriteLine((double)Colors.Blue);

        Console.WriteLine((int)LongColors.Red);
        Console.WriteLine((int)LongColors.Alpha);
        Console.WriteLine((int)LongColors.Green);
        Console.WriteLine((int)LongColors.Blue);
        Console.WriteLine((long)LongColors.Blue);
        Console.WriteLine((double)LongColors.Blue);
    }
}
