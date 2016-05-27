using System;

public class Classy
{
    public Classy() { }
}

public static class Program
{
    public static void Main(string[] Args)
    {
        Console.WriteLine("Number of arguments: " + Args.Length + ".");
        Console.WriteLine(Args.Length + " arguments.");
        Console.WriteLine("Created: " + new Classy());
    }
}
