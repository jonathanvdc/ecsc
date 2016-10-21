using System;
using Flame.Compiler;

namespace Flame.Ecs
{
    /// <summary>
    /// A value implementation that describes an erroneous value.
    /// </summary>
    public class ErrorValue : IValue
    {
        public ErrorValue(LogEntry Error)
        {
            this.Error = Error;
        }

        public LogEntry Error { get; private set; }

        /// <inheritdoc/>
        public IType Type { get { return ErrorType.Instance; } }

        /// <inheritdoc/>
        public ResultOrError<IExpression, LogEntry> CreateGetExpression(
            ILocalScope Scope, SourceLocation Location)
        {
            return ResultOrError<IExpression, LogEntry>.FromError(Error);
        }

        /// <inheritdoc/>
        public ResultOrError<IExpression, LogEntry> CreateAddressOfExpression(
            ILocalScope Scope, SourceLocation Location)
        {
            return ResultOrError<IExpression, LogEntry>.FromError(Error);
        }

        /// <inheritdoc/>
        public ResultOrError<IStatement, LogEntry> CreateSetStatement(
            IExpression Value, ILocalScope Scope, 
            SourceLocation Location)
        {
            return ResultOrError<IStatement, LogEntry>.FromError(Error);
        }
    }
}

