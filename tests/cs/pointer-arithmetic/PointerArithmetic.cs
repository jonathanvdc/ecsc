using System;
using System.Runtime.InteropServices;

public static class Program
{
    public unsafe static void Main()
    {
        var buf = Marshal.AllocHGlobal(4);
        var byteBuf = (byte*)buf;
        for (byte i = 0; i < 4; i++)
        {
            *(byteBuf + i) = i;
        }
        for (byte i = 0; i < 4; i++)
        {
            int val = *(byteBuf + i);
            Console.WriteLine(val);
        }
        Marshal.FreeHGlobal(buf);
    }
}