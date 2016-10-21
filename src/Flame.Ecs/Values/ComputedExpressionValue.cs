using System;
using Flame.Compiler;

namespace Flame.Ecs.Values
{
    /// <summary>
    /// A value that is obtained by building an expression when a
    /// get-expression is requested.
    /// </summary>
    public class ComputedExpressionValue : IValue
    {
        public ComputedExpressionValue(
            IType Type,
            Func<ILocalScope, SourceLocation, ResultOrError<IExpression, LogEntry>> CreateExpression)
        {
            this.CreateExpression = CreateExpression;
            this.Type = Type;
        }

        /// <summary>
        /// Creates a get-expression.
        /// </summary>
        public Func<ILocalScope, SourceLocation, ResultOrError<IExpression, LogEntry>> CreateExpression { get; private set; }

        /// <inheritdoc/>
        public IType Type { get; private set; }

        /// <inheritdoc/>
        public ResultOrError<IExpression, LogEntry> CreateGetExpression(
            ILocalScope Scope, SourceLocation Location)
        {
            return CreateExpression(Scope, Location);
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

