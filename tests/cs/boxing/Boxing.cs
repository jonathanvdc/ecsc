using System;

public struct Foo : ICloneable
{
    public int CloneCounter { get; private set; }

    public object Clone()
    {
        CloneCounter++;
        return this;
    }
}

public static class Program
{
    public static ICloneable BoxAndCast<T>(T Value)
    {
        return (ICloneable)Value;
    }

    public static void Main(string[] Args)
    {
        var foo = default(Foo);
        Console.WriteLine(((Foo)foo.Clone()).CloneCounter);
        Console.WriteLine(((Foo)BoxAndCast<Foo>(foo).Clone()).CloneCounter);
        Console.WriteLine(foo.CloneCounter);

        object i = 42;
        Console.WriteLine((int)i);
    }
}
