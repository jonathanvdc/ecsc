using System;

public static class Program
{
    public static void Main()
    {
        ulong value = 10;
        int intValue = value; // <-- this is not okay
        Console.WriteLine(intValue);
    }
}