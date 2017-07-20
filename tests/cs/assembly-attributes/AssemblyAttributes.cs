using System;
using System.Reflection;

[assembly: AssemblyTitle("AssemblyAttributes")]
[assembly: AssemblyDescription("A test assembly.")]

internal class Value
{

}

public static class Program
{
    public static void Main()
    {
        var customAttrs = new Value().GetType().Assembly.GetCustomAttributes(false);
        foreach (var item in customAttrs)
        {
            if (item is AssemblyTitleAttribute)
            {
                Console.WriteLine("Title: " + ((AssemblyTitleAttribute)item).Title);
            }
            else if (item is AssemblyDescriptionAttribute)
            {
                Console.WriteLine("Description: " + ((AssemblyDescriptionAttribute)item).Description);
            }
        }
    }
}