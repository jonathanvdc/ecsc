using static System.Console;

public static class Program
{
    public static double Fraction(int Count, int Total) => Count / Total;

    public static void Main()
    {
        WriteLine(Fraction(2, 3));
    }
}