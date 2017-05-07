using System;

public class A
{
    public A()
    { }
}

public class B : A
{
    public B()
    { }
}

public enum ByteEnum : byte
{
    Zero,
    One,
    Two,
    Three
}

public enum LongEnum : long
{
    Zero,
    One,
    Two,
    Three
}

public static class Program
{
    public static void Main()
    {
        // Test is/as
        B x = default(B);
        Console.WriteLine(x is A);
        Console.WriteLine(x is B);
        Console.WriteLine(x as A);
        Console.WriteLine(x as B);
        Console.WriteLine((A)x);
        Console.WriteLine((B)x);

        // Test that enum-to-enum conversions work.
        Console.WriteLine((int)(LongEnum)ByteEnum.Zero);
        Console.WriteLine((int)(LongEnum)ByteEnum.One);
        Console.WriteLine((int)(LongEnum)ByteEnum.Two);
        Console.WriteLine((int)(LongEnum)ByteEnum.Three);

        Console.WriteLine((int)(ByteEnum)LongEnum.Zero);
        Console.WriteLine((int)(ByteEnum)LongEnum.One);
        Console.WriteLine((int)(ByteEnum)LongEnum.Two);
        Console.WriteLine((int)(ByteEnum)LongEnum.Three);
    }
}
