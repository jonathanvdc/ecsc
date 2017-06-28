using static System.Console;
using IntWrapper = Box<int>;

public class Box<T>
{
    public Box(T value)
    {
        this.value = value;
    }

    public T value;
}

public static class Program
{
    public static void Main()
    {
        var wrapper = new IntWrapper(2);
        WriteLine(wrapper.value);
    }
}