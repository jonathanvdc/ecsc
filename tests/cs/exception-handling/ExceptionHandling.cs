using System;
using static System.Console;

public static class Program
{
    public static void Main()
    {
        try
        {
            throw new Exception();
        }
        catch (NullReferenceException)
        {
            WriteLine("What?");
        }
        catch (Exception e)
        {
            WriteLine("Caught exception: " + e.Message);
        }
        finally
        {
            WriteLine("Finally!");
        }
    }
}
