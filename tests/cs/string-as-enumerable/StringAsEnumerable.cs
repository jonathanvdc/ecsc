using System;
using System.Collections.Generic;

public static class Program
{
    public static void Main()
    {
        IEnumerable<char> seq = "hello world";
        foreach (var c in seq)
        {
            Console.Write(c);
        }
        Console.WriteLine();
    }
}