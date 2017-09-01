using System;

public class Finalizable
{
    ~Finalizable()
    {
        Console.WriteLine("Hello, finalizer!");
    }
}

public static class Program
{
    private static Finalizable data;

    public static void Main()
    {
        data = new Finalizable();
    }
}