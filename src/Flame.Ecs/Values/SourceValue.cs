using System;
using Flame.Compiler;
using Flame.Compiler.Expressions;
using Flame.Compiler.Statements;

namespace Flame.Ecs.Values
{
    /// <summary>
    /// A value implementation that embeds the source locations
    /// that are given by the Create* methods in the IR tree.
    /// </summary>
    public class SourceValue : IValue
    {
        public SourceValue(IValue Value)
        {
            this.Value = Value;
        }

        public IValue Value { get; private set; }

        /// <inheritdoc/>
        public IType Type { get { return Value.Type; } }

        /// <inheritdoc/>
        public ResultOrError<IExpression, LogEntry> CreateGetExpression(
            ILocalScope Scope, SourceLocation Location)
        {
            return Value.CreateGetExpression(Scope, Location)
                .MapResult(expr => SourceExpression.Create(expr, Location));
        }

        /// <inheritdoc/>
        public ResultOrError<IExpression, LogEntry> CreateAddressOfExpression(
            ILocalScope Scope, SourceLocation Location)
        {
            return Value.CreateAddressOfExpression(Scope, Location)
                .MapResult(expr => SourceExpression.Create(expr, Location));
        }

        /// <inheritdoc/>
        public ResultOrError<IStatement, LogEntry> CreateSetStatement(
            IExpression Value, ILocalScope Scope, 
            SourceLocation Location)
        {
            return this.Value.CreateSetStatement(Value, Scope, Location)
                .MapResult(stmt => SourceStatement.Create(stmt, Location));
        }
    }
}

