
public struct X
{
    public int x;
}

public static class Program
{
    public static X CreateValue()
    {
        return default(X);
    }

    public static void Main()
    {
        CreateValue().x = 10;
    }
}
