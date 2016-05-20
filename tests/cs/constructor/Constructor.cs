
public struct Vector2
{
    public Vector2(int x, int y)
    {
        this.x = x;
        this.y = y;
    }

    public int x, y;
}

public class Base
{
    public Base()
    {

    }

    public Base(int x)
    {
        this.x = x;
    }

    public int x;
}

public class Derived : Base
{
    public Derived(int x)
    {

    }

    // public Derived(ref int x)
    //     : base(x)
    // {

    // }

    public Derived(double x)
        : this((int)x)
    {

    }
}

public class Program
{
    public Program(ref int x)
    {
        return;
    }

    public static void Main()
    {

    }
}
