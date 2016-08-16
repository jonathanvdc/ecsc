using static System.Console;
using System.Collections.Generic;
using System.Threading.Tasks;

public static class Program
{
    private static object syncObj = new object();
    private static int counter = 0;

    public static void Count()
    {
        lock (syncObj)
        {
            for (int i = 0; i < 4; i++)
            {
                counter++;
                WriteLine(counter);
            }
        }
    }

    public static void Main()
    {
        // var tasks = new List<Task>();
        for (int i = 0; i < 4; i++)
        {
            // tasks.Add(Task.Run(Count));
            Count();
        }
        // Task.WaitAll(tasks.ToArray());
    }
}
