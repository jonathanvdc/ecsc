using System;
using Flame.Compiler;
using Flame.Compiler.Statements;

namespace Flame.Ecs.Values
{
    /// <summary>
    /// A value that is a plain wrapper for an 
    /// expression and its location in the source code.
    /// </summary>
    public class ExpressionValue : IValue
    {
        public ExpressionValue(IExpression Expression)
        {
            this.Expression = Expression;
        }

        /// <summary>
        /// Gets the expression that defines this value.
        /// </summary>
        /// <value>The expression.</value>
        public IExpression Expression { get; private set; }

        /// <inheritdoc/>
        public IType Type { get { return Expression.Type; } }

        /// <inheritdoc/>
        public ResultOrError<IExpression, LogEntry> CreateGetExpression(
            ILocalScope Scope, SourceLocation Location)
        {
            return ResultOrError<IExpression, LogEntry>.FromResult(Expression);
        }

        /// <inheritdoc/>
        public ResultOrError<IExpression, LogEntry> CreateAddressOfExpression(
            ILocalScope Scope, SourceLocation Location)
        {
            return ResultOrError<IExpression, LogEntry>.FromError(
                new LogEntry(
                    "invalid operation",
                    "this expression's address cannot be retrieved.",
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

