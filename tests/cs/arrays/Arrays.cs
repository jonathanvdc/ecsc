using System;

public static class Program
{
    private static void PrintAll(int[] Items)
    {
        for (int i = 0; i < Items.Length; i++)
        {
            Console.WriteLine(Items[i]);
        }
    }

    public static void Main(string[] Args)
    {
        for (int i = 0; i < Args.Length; i++)
        {
            Console.WriteLine(Args[i]);
        }

        PrintAll(new[] { 1, 2, 3 });
        PrintAll(new int[] { 1, 2, 3 });
        PrintAll(new int[3] { 1, 2, 3 });
        PrintAll(new int[3]);
    }
}
