using System;
using System.Collections.Generic;

public static class Program
{
    public static void Main()
    {
        int[] arr = new int[1];
        arr[0] = 1;
        Console.WriteLine(arr[0]);

        var list = new List<int>() { 0 };
        list[0] = 1;
        Console.WriteLine(list[0]);
    }
}
