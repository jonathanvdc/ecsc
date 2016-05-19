using System;

public static class Program
{
    public static void IncDecInt8()
    {
        sbyte i = (sbyte)0;
        Console.WriteLine((int)i++);
        Console.WriteLine((int)++i);
        Console.WriteLine((int)i--);
        Console.WriteLine((int)--i);
        Console.WriteLine((int)i);
    }

    public static void IncDecInt16()
    {
        short i = (short)0;
        Console.WriteLine((int)i++);
        Console.WriteLine((int)++i);
        Console.WriteLine((int)i--);
        Console.WriteLine((int)--i);
        Console.WriteLine((int)i);
    }

    public static void IncDecInt32()
    {
        int i = 0;
        Console.WriteLine(i++);
        Console.WriteLine(++i);
        Console.WriteLine(i--);
        Console.WriteLine(--i);
        Console.WriteLine(i);
    }

    public static void IncDecInt64()
    {
        long i = (long)0;
        Console.WriteLine(i++);
        Console.WriteLine(++i);
        Console.WriteLine(i--);
        Console.WriteLine(--i);
        Console.WriteLine(i);
    }

    public static void IncDecUInt8()
    {
        byte i = (byte)0;
        Console.WriteLine((int)i++);
        Console.WriteLine((int)++i);
        Console.WriteLine((int)i--);
        Console.WriteLine((int)--i);
        Console.WriteLine((int)i);
    }

    public static void IncDecUInt16()
    {
        ushort i = (ushort)0;
        Console.WriteLine((int)i++);
        Console.WriteLine((int)++i);
        Console.WriteLine((int)i--);
        Console.WriteLine((int)--i);
        Console.WriteLine((int)i);
    }

    public static void IncDecUInt32()
    {
        uint i = (uint)0;
        Console.WriteLine(i++);
        Console.WriteLine(++i);
        Console.WriteLine(i--);
        Console.WriteLine(--i);
        Console.WriteLine(i);
    }

    public static void IncDecUInt64()
    {
        ulong i = (ulong)0;
        Console.WriteLine(i++);
        Console.WriteLine(++i);
        Console.WriteLine(i--);
        Console.WriteLine(--i);
        Console.WriteLine(i);
    }

    public static void IncDecFloat32()
    {
        float i = 0.0f;
        Console.WriteLine(i++);
        Console.WriteLine(++i);
        Console.WriteLine(i--);
        Console.WriteLine(--i);
        Console.WriteLine(i);
    }

    public static void IncDecFloat64()
    {
        double i = 0.0;
        Console.WriteLine(i++);
        Console.WriteLine(++i);
        Console.WriteLine(i--);
        Console.WriteLine(--i);
        Console.WriteLine(i);
    }

    public static void Main()
    {
        IncDecInt8();
        IncDecInt16();
        IncDecInt32();
        IncDecInt64();

        IncDecUInt8();
        IncDecUInt16();
        IncDecUInt32();
        IncDecUInt64();

        IncDecFloat32();
        IncDecFloat64();
    }
}
