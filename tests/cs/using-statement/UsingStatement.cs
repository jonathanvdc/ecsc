using System;

public class Resource : IDisposable
{
    public Resource()
    {
        Console.WriteLine("Creating resource");
    }

    public void Dispose()
    {
        Console.WriteLine("Disposing resource");
    }

    public override string ToString()
    {
        return "I lied. I actually don't manage any resources at all.";
    }
}

public static class Program
{
    public static void Main(string[] Args)
    {
        using (var r = new Resource())
        {
            Console.WriteLine(r);
        }
    }
}
