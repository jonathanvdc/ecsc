using System;
using System.Collections.Generic;

public class Enumerator
{
    public Enumerator() { }

    public int Current { get; private set; } = 10;

    public bool MoveNext()
    {
        Current--;
        return Current > 0;
    }

    public void Dispose()
    {
        Console.WriteLine("Hi!");
    }
}

public class Enumerable
{
    public Enumerable() { }

    public Enumerator GetEnumerator()
    {
        return new Enumerator();
    }
}

public static class Program
{
    public static void Main(string[] Args)
    {
        foreach (var item in Args)
            Console.WriteLine(item);

        foreach (var item in new List<string>(Args))
            Console.WriteLine(item);

        foreach (var item in new Enumerable())
            Console.WriteLine(item);
    }
}
