using System;
using System.Collections.Generic;
using System.Linq;

public class Herp
{
    public Herp(int X)
    {
        this.X = X;
    }

    public int X { get; private set; }
}

public static class HerpExtensions
{
    public static void PrintX(this Herp Value)
    {
        Console.WriteLine(Value.X);
    }
}

public static class Program
{
    public static void Main()
    {
        var herp = new Herp(20);
        herp.PrintX();

        var items = new List<int>();
        items.Add(10);
        items.Add(20);
        items.Add(30);
        // Note that ToArray<int> is actually a generic extension method:
        // Enumerable.ToArray<T>.
        foreach (var x in items.ToArray<int>())
        {
            Console.WriteLine(x);
        }
    }
}
