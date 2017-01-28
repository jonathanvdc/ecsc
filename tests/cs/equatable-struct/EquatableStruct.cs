using System;
using System.Collections.Generic;

public struct EquatableStruct : IEquatable<EquatableStruct>
{
    public EquatableStruct(int Data)
    {
        this.Data = Data;
    }

    public readonly int Data;

    public override bool Equals(object Other)
    {
        return Equals((EquatableStruct)Other);
    }

    public bool Equals(EquatableStruct Other)
    {
        return Data == Other.Data;
    }

    public override int GetHashCode()
    {
        return Data;
    }
}

public static class Program
{
    public static void Main(string[] Args)
    {
        var eqStruct1 = new EquatableStruct(1);
        var eqStruct2 = new EquatableStruct(2);
        Console.WriteLine(eqStruct1.Equals(eqStruct1));
        Console.WriteLine(eqStruct1.Equals(eqStruct2));
        Console.WriteLine(eqStruct2.Equals(eqStruct1));
        Console.WriteLine(eqStruct2.Equals(eqStruct2));
    }
}
