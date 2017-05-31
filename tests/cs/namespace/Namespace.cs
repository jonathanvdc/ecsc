using System;

namespace Lib
{
    public sealed class Box<T>
    {
        public T value;
    }
}

public static class Program
{
    public static void Main()
    {
        var b = new Lib.Box<int>();
        b.value = 10;
        Console.WriteLine(b.value);
    }
}