using System;

public struct Vector2
{
    public Vector2(double X, double Y)
    {
        this.x = X;
        this.Y = Y;
    }

    private double x;
    public double X { get { return x; } private set { x = value; } }
    public double Y { get; private set; }

    public double this[int i]
    {
        get { return i == 1 ? Y : X; }
        private set
        {
            if (i == 1)
                Y = value;
            else
                X = value;
        }
    }

    public double this[uint i]
    {
        get => this[(int)i];
        private set => this[(uint)i] = value;
    }

    public double this[long i] => i == 1 ? Y : X;

    public double LengthSquared
    {
        get { return X * X + Y * Y; }
    }

    public double Length => Math.Sqrt(LengthSquared);
}

public static class Program
{
    public static void Main(string[] Args)
    {
        var vec = new Vector2(3, 4);
        Console.WriteLine(vec.X);
        Console.WriteLine(vec.Y);
        Console.WriteLine(vec[0]);
        Console.WriteLine(vec[1]);
        Console.WriteLine(vec[0L]);
        Console.WriteLine(vec[1L]);
        Console.WriteLine(vec.LengthSquared);
        Console.WriteLine(vec.Length);
    }
}
