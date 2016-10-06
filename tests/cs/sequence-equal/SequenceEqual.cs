using System;
using System.Collections.Generic;
using System.Linq;

public static class Program
{
    public static void Main()
    {
        var l1 = new List<string>() { "hi", "hello" };
        var l2 = new List<string>() { "hi", "hello" };
        Console.WriteLine(Enumerable.SequenceEqual<string>(l1, l2));
    }
}
