using System;

public static class ArithmeticTest
{
    public static int Add(int x, int y) => x + y;
    public static int Sub(int x, int y) => x - y;
    public static int Mul(int x, int y) => x * y;
    public static int Div(int x, int y) => x / y;
    public static int Rem(int x, int y) => x % y;
    public static int And(int x, int y) => x & y;
    public static int Or(int x, int y) => x | y;
    public static int Xor(int x, int y) => x ^ y;


    public static void Main()
    {
        Console.WriteLine(ArithmeticTest.Add(3, 4));
        Console.WriteLine(ArithmeticTest.Sub(3, 4));
        Console.WriteLine(ArithmeticTest.Mul(3, 4));
        Console.WriteLine(ArithmeticTest.Div(4, 3));
        Console.WriteLine(ArithmeticTest.Rem(4, 3));
        Console.WriteLine(ArithmeticTest.And(4, 3));
        Console.WriteLine(ArithmeticTest.Or(4, 3));
        Console.WriteLine(ArithmeticTest.Xor(4, 3));
    }
}
