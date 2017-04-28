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
            this.Error = new Lazy<LogEntry>(() => Error);
        }
        public ErrorValue(Lazy<LogEntry> Error)
        {
            this.Error = Error;
        }
        public ErrorValue(Func<LogEntry> ErrorFactory)
        {
            this.Error = new Lazy<LogEntry>(ErrorFactory);
        }

        public Lazy<LogEntry> Error { get; private set; }

        /// <inheritdoc/>
        public IType Type { get { return ErrorType.Instance; } }

        /// <inheritdoc/>
        public ResultOrError<IExpression, LogEntry> CreateGetExpression(
            ILocalScope Scope, SourceLocation Location)
        {
            return ResultOrError<IExpression, LogEntry>.FromError(Error.Value);
        }

        /// <inheritdoc/>
        public ResultOrError<IExpression, LogEntry> CreateAddressOfExpression(
            ILocalScope Scope, SourceLocation Location)
        {
            return ResultOrError<IExpression, LogEntry>.FromError(Error.Value);
        }

        /// <inheritdoc/>
        public ResultOrError<IStatement, LogEntry> CreateSetStatement(
            IExpression Value, ILocalScope Scope, 
            SourceLocation Location)
        {
            return ResultOrError<IStatement, LogEntry>.FromError(Error.Value);
        }
    }
}

