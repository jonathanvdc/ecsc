using System;

namespace Lib
{
    public sealed class Box<T>
    {
        public T value;
    }
}

namespace Lib.BoxTricks
{
    public static class BoxUtils
    {
        public static void Print<T>(Box<T> box)
        {
            Console.WriteLine("box [{0}]", box.value);
        }
    }
}

public static class Program
{
    public static void Main()
    {
        var b = new Lib.Box<int>();
        b.value = 10;
        Lib.BoxTricks.BoxUtils.Print<int>(b);
    }
}