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
    public static complex operator *(complex a, double b)
    {
        return new complex(a.r * b, a.i * b);
    }
    public static complex operator /(complex a, double b)
    {
        return new complex(a.r / b, a.i / b);
    }
    public static complex operator <<(complex a, int b)
    {
        return a * Math.Pow(2, b);
    }
    public static complex operator >>(complex a, int b)
    {
        return a / Math.Pow(2, b);
    }
}

public class Program
{
    public static void Main(string[] Args)
    {
        var c = new complex(3, 4);
        c = c + new complex(4, 4);
        c += c;
        c = c >> 1;
        c = c << 1;
        Console.WriteLine(c.r);
        Console.WriteLine(c.i);
        Console.WriteLine(c.sqabs());
    }
}
