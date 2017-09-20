// This test was adapted from a code sample from the C# language reference:
// https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/goto

using System;

public static class Program
{
    public static void Main()
    {
        int x = 200, y = 4;
        int count = 0;
        string[] array = new string[x * y];

        // Initialize the array:
        for (int i = 0; i < x; i++)
            for (int j = 0; j < y; j++)
                array[i * y + j] = (++count).ToString();

        // Input a string:
        string myNumber = "49";

        // Search:
        for (int i = 0; i < x; i++)
        {
            for (int j = 0; j < y; j++)
            {
                if (array[i * y + j].Equals(myNumber))
                {
                    goto Found;
                }
            }
        }

        Console.WriteLine("The number " + myNumber + " was not found.");
        goto Finish;

    Found:
        Console.WriteLine("The number " + myNumber + " is found.");

    Finish:
        Console.WriteLine("End of search.");
    }
}