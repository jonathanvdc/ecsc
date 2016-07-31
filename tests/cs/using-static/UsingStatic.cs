// "Official" sample from
// https://github.com/dotnet/roslyn/wiki/New-Language-Features-in-C%23-6

using static System.Console;
using static System.Math;
using static System.DayOfWeek;

class Program
{
    static void Main()
    {
        WriteLine(Sqrt(3*3 + 4*4));
        WriteLine(Friday - Monday);
    }
}
