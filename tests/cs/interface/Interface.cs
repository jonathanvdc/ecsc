using System;

interface IFlyable
{
    void Fly();
    string Name { get; }
    string this[int i] { get; }
}

class Bird : IFlyable
{
    public Bird()
    {
        Name = "Bird";
    }

    public string Name { get; private set; }

    public string this[int i] => Name;

    public void Fly()
    {
        Console.WriteLine("Chirp");
    }
}

class Plane : IFlyable
{
    public Plane() { }

    public string Name => "Plane";

    public string this[int i] => Name;

    public void Fly()
    {
        Console.WriteLine("Nnneeaoowww");
    }
}

static class Program
{
    public static IFlyable[] GetBirdInstancesAndPlaneInstancesMixed()
    {
        return new IFlyable[]
        {
            new Bird(),
            new Plane()
        };
    }

    public static void Main(string[] Args)
    {
        var items = GetBirdInstancesAndPlaneInstancesMixed();
        for (int i = 0; i < items.Length; i++)
        {
            Console.Write(items[i].Name + ": ");
            items[i].Fly();
        }
    }
}
