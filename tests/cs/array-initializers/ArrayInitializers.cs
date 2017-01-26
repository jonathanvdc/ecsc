using System;

public static class Program
{
    private static int[] globalIntArray = { 1, 2, 3, 4, 5 };
    private static object[] globalObjectArray = { 2, "test", 2.45, true, 6.9f };

    private static void PrintAll(int[] Array)
    {
        foreach (var item in Array)
        {
            Console.WriteLine(item);
        }
    }

    private static void PrintAll(object[] Array)
    {
        foreach (var item in Array)
        {
            Console.WriteLine(item);
        }
    }

    public static void Main(string[] Args)
    {
        int[] localIntArray = { 1, 2, 3, 4, 5 };
        object[] localObjectArray = { 2, "test", 2.45, true, 6.9f };

        PrintAll(localIntArray);
        PrintAll(localObjectArray);
        PrintAll(globalIntArray);
        PrintAll(globalObjectArray);
    }
}
