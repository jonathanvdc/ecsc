using System;

struct complex
{
    public complex(double a, double b) { r = a; i = b; }
    public double r;
    public double i;

    public complex square()
    {
        return new complex(r * r - i * i, 2.0 * r * i);
    }
    public double sqabs()
    {
        return r * r + i * i;
    }
    public static complex operator +(complex a, complex b)
    {
        return new complex(a.r + b.r, a.i + b.i);
    }
}

public class Program
{
    public static void Main(string[] Args)
    {
        var c = new complex(3, 4);
        c = c + new complex(4, 4);
        c += c;
        Console.WriteLine(c.r);
        Console.WriteLine(c.i);
        Console.WriteLine(c.sqabs());
    }
}
