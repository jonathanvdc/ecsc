using Stuff = System.Console;

public class C
{
    public static int Method1 (string x, int y) { return 0; }
    public static int Method1 (string x, string y) { return 0; }
    public int Method2 (int z)
    {
        Stuff.WriteLine(nameof(z));
        return 0;
    }

    public string f<T>() => nameof(T);
}

public static class Program
{
    public static void Main()
    {
        var c = new C();

        Stuff.WriteLine(nameof(C)); // -> "C"
        Stuff.WriteLine(nameof(C.Method1)); // -> "Method1"
        Stuff.WriteLine(nameof(C.Method2)); // -> "Method2"
        Stuff.WriteLine(nameof(c.Method1)); // -> "Method1"
        Stuff.WriteLine(nameof(c.Method2)); // -> "Method2"
        Stuff.WriteLine(nameof(Stuff)); // -> "Stuff"
        Stuff.WriteLine(nameof(C.f)); // -> "f"
    }
}