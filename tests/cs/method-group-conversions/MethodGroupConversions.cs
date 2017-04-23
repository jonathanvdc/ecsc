using System;
using System.Collections.Generic;
using System.Linq;

public static class Program
{
    private static T Id<T>(T Value)
    {
        return Value;
    }

    private static IEnumerable<T> Id<T>(IEnumerable<T> Values)
    {
        // Implicit method group conversion:
        //
        //     IEnumerable<T> Id<T>(IEnumerable<T>) & T Id<T>(T)
        //
        //     -->
        //
        //     Func<T, T>
        //
        return Enumerable.Select<T, T>(Values, Id<T>);
    }

    public static void Main()
    {
        var list = new string[] { "Hello, world!" };
        var idFunc = (Func<IEnumerable<string>, IEnumerable<string>>)Id<string>;
        foreach (var item in idFunc(list).ToArray<string>())
        {
            Console.WriteLine(item);
        }
    }
}