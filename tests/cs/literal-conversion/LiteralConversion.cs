using static System.Console;

public static class Program
{
    public static void Main()
    {
        ulong x = 10;
        WriteLine((x & 0x2) == 1);
    }
}