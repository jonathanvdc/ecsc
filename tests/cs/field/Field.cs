using System;

public struct Vector2
{
    public double x, y;
}

public static class Program
{
    public static void Main()
    {
        var vec = default(Vector2);
        vec.x = vec.y = 3.0;
        Console.WriteLine(vec.x);
        Console.WriteLine(vec.y);
    }
}
