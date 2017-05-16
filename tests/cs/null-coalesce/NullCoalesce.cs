using System;
using System.Collections.Generic;

public static class Program
{
    public static void Main()
    {
        List<int> list = null;
        list = list ?? new List<int>() { 1 };
        Console.WriteLine((list ?? new List<int>()).Count);
        list = null;
        Console.WriteLine((list ?? new List<int>()).Count);
    }
}