using System;
using System.Collections.Generic;

public static class Program
{
    public static IEnumerable<T> Repeat<T>(T Value, int Times)
    {
        for (int i = 0; i < Times; i++)
            yield return Value;

        yield break;
    }

    public static void Main()
    {
        foreach (var item in Repeat<int>(5, 3))
            Console.WriteLine(item);
    }
}