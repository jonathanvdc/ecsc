using System;

public static class Program
{
    public static unsafe void Main()
    {
        int local = 10;
        int* localPtr = &local;
        *localPtr = 42;
        Console.WriteLine(*localPtr);

        void* voidPtr = localPtr;
        localPtr = (int*)voidPtr;
        Console.WriteLine(*localPtr);
    }
}