using System;

public static class C
{
    public static V F<T, V>(T X, V Y)
    {
        var inst = new { X, Y };
        return inst.Y;
    }
}

public static class D<T>
{
    public static T F(T X)
    {
        var inst = new { X };
        return inst.X;
    }
}

public static class Program
{
    public static void Main(string[] Args)
    {
        var inst = new { X = 3, Y = 4 };
        Console.WriteLine(inst.X);
        Console.WriteLine(inst.Y);
        var inst2 = new { inst, inst.Y };
        Console.WriteLine(inst2.inst.X);
        Console.WriteLine(inst2.Y);
        Console.WriteLine(C.F<int, double>(42, 17.5));
        Console.WriteLine(D<int>.F(42));
    }
}
