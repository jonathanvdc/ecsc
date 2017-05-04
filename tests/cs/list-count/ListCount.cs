using System;
using System.Collections.Generic;
using System.Linq;

public static class Program
{
    public static void Main()
    {
        // This test verifies that `list.Count` is resolved as
        // `System.Collections.List<T>.Count`, not as an extension
        // method.
        var list = new List<int>() { 1, 2, 3 };
        if (list.Count > 0)
        {
            Console.WriteLine(list.Count);
        }
    }
}