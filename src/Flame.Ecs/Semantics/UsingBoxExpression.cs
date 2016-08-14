using System;
using Flame.Compiler;
using Flame.Compiler.Expressions;

namespace Flame.Ecs.Semantics
{
    /// <summary>
    /// A box-expression that was created by a 'using' cast.
    /// The boxing operation is to be elided if a delegate is
    /// created from this value.
    /// </summary>
    public class UsingBoxExpression : ComplexExpressionBase
    {
        public UsingBoxExpression(
            IExpression Value, IType Type)
        {
            this.Value = Value;
            this.ty = Type;
        }

        /// <summary>
        /// Gets the value that is boxed by this expression.
        /// </summary>
        /// <value>The value to box.</value>
        public IExpression Value { get; private set; }

        private IType ty;

        /// <summary>
        /// Gets the type of object that the expression will return.
        /// </summary>
        /// <value>The type.</value>
        public override IType Type
        {
            get
            {
                return ty;
            }
        }

        protected override IExpression Lower()
        {
            return new StaticCastExpression(Value, ty);
        }

        public override string ToString()
        {
            return string.Format("using-box({0}, {1})", Value, Type);
        }
    }
}

