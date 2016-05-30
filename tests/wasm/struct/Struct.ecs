
public static class spectest
{
    /// <summary>
    /// Prints a number to standard output.
    /// </summary>
    public static extern void print(int Value);
}

public struct Vector2
{
    public Vector2(int X, int Y)
    {
        this = default(Vector2);
        this.X = X;
        this.Y = Y;
    }

    public int X { get; private set; }
    public int Y { get; private set; }
    public int LengthSquared => X * X + Y * Y;
}

public static class Program
{
    public static void Main()
    {
        var vec = new Vector2(3, 4);
        spectest.print(vec.X);
        spectest.print(vec.Y);
        spectest.print(vec.LengthSquared);
    }
}
