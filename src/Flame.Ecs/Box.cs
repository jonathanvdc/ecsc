using System;

namespace Flame.Ecs
{
    /// <summary>
    /// Defines a 'box' object: a reference type
    /// that contains a value.
    /// </summary>
    public class Box<T>
    {
        public Box(T Value)
        {
            this.Value = Value;
        }

        public T Value;
    }
}

