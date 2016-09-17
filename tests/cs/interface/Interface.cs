using System;

interface IFlyable
{
    void Fly();
}

class Bird : IFlyable
{
    public Bird() { }

    public void Fly()
    {
        Console.WriteLine("Chirp");
    }
}

class Plane : IFlyable
{
    public Plane() { }

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
            items[i].Fly();
        }
    }
}
