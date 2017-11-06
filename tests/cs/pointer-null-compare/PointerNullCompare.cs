using static System.Console;

public static unsafe class Program
{
    public static void Main()
    {
        WriteLine((void*)null == null);
    }
}