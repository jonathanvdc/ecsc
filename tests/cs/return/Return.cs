
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
}
