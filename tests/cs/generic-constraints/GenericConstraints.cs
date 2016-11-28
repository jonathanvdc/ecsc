using System;

public sealed class BinaryTree<T>
    where T : class, IComparable
{
    public BinaryTree(
        T Value,
        BinaryTree<T> Left,
        BinaryTree<T> Right)
    {
        this.Value = Value;
        this.Left = Left;
        this.Right = Right;
    }

    public BinaryTree(T Value)
        : this(Value, null, null)
    { }

    public T Value { get; private set; }
    public BinaryTree<T> Left { get; private set; }
    public BinaryTree<T> Right { get; private set; }

    public bool Contains(T OtherValue)
    {
        int cmp = OtherValue.CompareTo(Value);
        if (cmp == 0)
            return true;
        else if (cmp > 0)
            if (Right == null)
                return false;
            else
                return Right.Contains(OtherValue);
        else
            if (Left == null)
                return false;
            else
                return Left.Contains(OtherValue);
    }
}

public class ComparableValue : IComparable, IComparable<ComparableValue>
{
    public ComparableValue(int Value)
    {
        this.Value = Value;
    }

    public int Value { get; private set; }

    public int CompareTo(ComparableValue Other)
    {
        if (Value == Other.Value)
            return 0;
        else if (Value > Other.Value)
            return 1;
        else
            return -1;
    }

    public int CompareTo(object Other)
    {
        return CompareTo((ComparableValue)Other);
    }
}

public static class Program
{
    public static void Main(string[] Args)
    {
        var c1 = new ComparableValue(1);
        var c2 = new ComparableValue(2);
        var c3 = new ComparableValue(3);
        var tree = new BinaryTree<ComparableValue>(
            c2,
            new BinaryTree<ComparableValue>(c1),
            new BinaryTree<ComparableValue>(c3));
        Console.WriteLine(tree.Contains(c1));
    }
}
