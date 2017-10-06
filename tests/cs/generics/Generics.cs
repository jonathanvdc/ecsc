using System;

public struct Vector2<T>
{
    public Vector2(T X, T Y)
    {
        this.X = X;
        this.Y = Y;
    }

    public T X, Y;

    public override bool Equals(object obj)
    {
        return obj is Vector2<T>
            && object.Equals(X, ((Vector2<T>)obj).X)
            && object.Equals(Y, ((Vector2<T>)obj).Y);
    }
}

public static class Program
{
    public static void Main()
    {
        var vec = default(Vector2<int>);
        vec.X = 3;
        vec.Y = 4;
        Console.WriteLine(vec.X);
        Console.WriteLine(vec.Y);
        vec = new Vector2<int>(4, 3);
        Console.WriteLine(vec.X);
        Console.WriteLine(vec.Y);
    }
}
