using static System.Console;

public class Counter
{
    public Counter() { }

    public int Count;

    public Counter Increment()
    {
        Count++;
        return this;
    }

    public Counter Decrement<T>()
    {
        Count--;
        return this;
    }
}

public static class Program
{
    public static readonly Counter printCounter = new Counter();

    public static void Main()
    {
        WriteLine(printCounter.Increment().Coutn);
        WriteLine(printCounter.Inrement().Count);
        WriteLine(printCoutner.Increment().Count);
        WriteLine(printCounter.Dcrement<int>().Count);
    }
}