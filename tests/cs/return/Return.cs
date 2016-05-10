
public static class ReturnTest
{
    public static void ReturnVoid()
    {
        return;
    }

    public static int ReturnValue(int Value)
    {
        return Value;
    }

    public static T ReturnGeneric<T>(T Value)
    {
        return Value;
    }

    public static void Main()
    {
        ReturnTest.ReturnVoid();
        System.Console.WriteLine(ReturnValue(4));
        System.Console.WriteLine(ReturnTest.ReturnGeneric<int>(2));
    }
}
