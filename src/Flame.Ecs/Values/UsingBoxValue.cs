using Flame.Compiler;
using Flame.Compiler.Expressions;

namespace Flame.Ecs.Values
{
    /// <summary>
    /// A value that performs a using-box operation on another value.
    /// </summary>
    public class UsingBoxValue : IValue
    {
        public UsingBoxValue(IValue BoxedValue, IType Type)
        {
            this.BoxedValue = BoxedValue;
            this.Type = Type;
        }

        /// <summary>
        /// Gets the value that is boxed by this value.
        /// </summary>
        /// <value>The boxed value.</value>
        public IValue BoxedValue { get; private set; }

        /// <inheritdoc/>
        public IType Type { get; private set; }

        /// <summary>
        /// Gets the value that is boxed by the given value, or null if
        /// the given value is not a using-box.
        /// </summary>
        /// <param name="Value">The top-level value to inspect.</param>
        /// <returns>The value that is boxed, or null if the given value is not a using-box.</returns>
        public static IValue GetBoxedValue(IValue Value)
        {
            if (Value is UsingBoxValue)
                return ((UsingBoxValue)Value).BoxedValue;
            else if (Value is SourceValue)
                return GetBoxedValue(((SourceValue)Value).Value);
            else
                return null;
        }

        /// <inheritdoc/>
        public ResultOrError<IExpression, LogEntry> CreateGetExpression(
            ILocalScope Scope, SourceLocation Location)
        {
            return BoxedValue.CreateGetExpression(Scope, Location).MapResult<IExpression>(
                expr => new ReinterpretCastExpression(new BoxExpression(expr), Type));
        }

        /// <inheritdoc/>
        public ResultOrError<IExpression, LogEntry> CreateAddressOfExpression(
            ILocalScope Scope, SourceLocation Location)
        {
            return ResultOrError<IExpression, LogEntry>.FromError(
                new LogEntry(
                    "invalid operation",
                    "this expression does not have an address.",
                    Location));
        }

        /// <inheritdoc/>
        public ResultOrError<IStatement, LogEntry> CreateSetStatement(
            IExpression Value, ILocalScope Scope,
            SourceLocation Location)
        {
            return ResultOrError<IStatement, LogEntry>.FromError(
                new LogEntry(
                    "malformed assignment",
                    "the left-hand side of an assignment must be a variable, a property or an indexer.",
                    Location));
        }
    }
}

