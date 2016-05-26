using System;

// Implicitly internal
struct Vector2
{
    internal Vector2(int x, int y)
    {
        this.x = x;
        this.y = y;
    }

    internal int x, y;
}

// Explicitly marked internal
internal class Program
{
    public static void Main()
    {
        var vec = new Vector2(3, 4);
        Console.WriteLine(vec.x);
        Console.WriteLine(vec.y);
    }
}
