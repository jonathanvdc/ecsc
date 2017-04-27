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
        var iterator = Repeat<int>(5, 3).GetEnumerator();
        while (iterator.MoveNext())
        {
            Console.WriteLine(iterator.Current);
        }
    }
}