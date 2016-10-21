using System;
using Flame.Compiler;
using Flame.Compiler.Statements;

namespace Flame.Ecs.Values
{
    /// <summary>
    /// A value implementation that wraps a variable.
    /// A variable is deemed to be incapable of performing
    /// a certain operation if it returns null for that 
    /// operation, which will be caught and reported 
    /// by this value implementation.
    /// </summary>
    public class VariableValue : IValue
    {
        public VariableValue(IVariable Variable)
        {
            this.Variable = Variable;
        }

        /// <summary>
        /// Gets the variable that is wrapped by this
        /// instance.
        /// </summary>
        /// <value>The variable.</value>
        public IVariable Variable { get; private set; }

        /// <inheritdoc/>
        public IType Type { get { return Variable.Type; } }

        /// <inheritdoc/>
        public ResultOrError<IExpression, LogEntry> CreateGetExpression(
            ILocalScope Scope, SourceLocation Location)
        {
            var result = Variable.CreateGetExpression();
            if (result == null)
            {
                return ResultOrError<IExpression, LogEntry>.FromError(
                    new LogEntry(
                        "invalid operation",
                        "this variable does not have a (retrievable) value.",
                        Location));
            }
            else
            {
                return ResultOrError<IExpression, LogEntry>.FromResult(result);
            }
        }

        /// <inheritdoc/>
        public ResultOrError<IExpression, LogEntry> CreateAddressOfExpression(
            ILocalScope Scope, SourceLocation Location)
        {
            var result = Variable is IUnmanagedVariable
                ? ((IUnmanagedVariable)Variable).CreateAddressOfExpression()
                : null;

            if (result == null)
            {
                return ResultOrError<IExpression, LogEntry>.FromError(
                    new LogEntry(
                        "invalid operation",
                        "this variable does not have a (retrievable) address.",
                        Location));
            }
            else
            {
                return ResultOrError<IExpression, LogEntry>.FromResult(result);
            }
        }

        /// <inheritdoc/>
        public ResultOrError<IStatement, LogEntry> CreateSetStatement(
            IExpression Value, ILocalScope Scope, 
            SourceLocation Location)
        {
            var result = Variable.CreateSetStatement(Value);

            if (result == null)
            {
                return ResultOrError<IStatement, LogEntry>.FromError(
                    new LogEntry(
                        "invalid operation",
                        "no value can be stored in this variable.",
                        Location));
            }
            else
            {
                return ResultOrError<IStatement, LogEntry>.FromResult(result);
            }
        }
    }
}

