using System;

public static class Program
{
    public const uint PageSize = 64 * 1024;

    public static void Main()
    {
        Console.WriteLine(PageSize);
        byte x = 1;
        x |= 0x80;
        Console.WriteLine(x);
    }
}