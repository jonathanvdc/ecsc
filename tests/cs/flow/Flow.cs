using System;

public static class Program
{
    public static int Select(bool C, int X, int Y)
    {
        return C ? X : Y;
    }

    public static int IfElse(bool C, int X, int Y)
    {
        if (C)
            return X;
        else
            return Y;
    }

    public static int If(bool C, int X, int Y)
    {
        if (C)
            return X;

        return Y;
    }

    public static int Count(int N)
    {
        int i = 0;
        while (i < N)
            i++;

        return i;
    }
    
    public static int CountBreakContinue(int N)
    {
        int i = 0;
        while (true)
        {
            if (i >= N)
            break;

            i++;
            continue;
        }

        return i;
    }

    public static int Sum(int N)
    {
        int j = 0;
        for (int i = 0; i < N; i++)
            j += i;

        return j;
    }

    public static void Main(string[] Args)
    {
        Console.WriteLine(Select(true, 3, 4));
        Console.WriteLine(IfElse(false, 3, 4));
        Console.WriteLine(If(true, 3, 4));
        Console.WriteLine(Count(17));
        Console.WriteLine(CountBreakContinue(17));
        Console.WriteLine(Sum(17));
    }
}
