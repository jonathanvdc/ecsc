using static System.Console;

public static class Program
{
    public static uint Fibonacci(uint n)
    {
        uint result;
        switch (n)
        {
            case 0:
            case 1:
                result = 1u;
                break;
            default:
                result = Fibonacci(n - 1u) + Fibonacci(n - 2u);
                break;
        }
        return result;
    }

    public static void Main()
    {
        WriteLine(Fibonacci(20));
    }
}