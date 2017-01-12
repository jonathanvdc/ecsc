using System;

public struct Point
{
    public Point(int X, int Y)
    {
        this = default(Point);
        this.X = X;
        this.Y = Y;
    }
    public Point(ref Point Other)
    {
        this = default(Point);
        this.X = Other.X;
        this.Y = Other.Y;
    }

    public readonly int X;
    public readonly int Y;

    public override string ToString()
    {
        return "(" + X + ", " + Y + ")";
    }
}

public static class Program
{
    public static void Main(string[] Args)
    {
        Point pt1 = default(Point), pt2 = default(Point);
        pt1 = new Point(1, 2);
        pt2 = new Point(ref pt1);
        pt1 = new Point(ref pt1);
        Console.WriteLine(pt1);
        Console.WriteLine(pt2);
    }
}
