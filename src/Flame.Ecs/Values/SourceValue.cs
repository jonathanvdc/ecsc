using System;
using Flame.Compiler;
using Flame.Compiler.Expressions;
using Flame.Compiler.Statements;

namespace Flame.Ecs.Values
{
    /// <summary>
    /// A value implementation that replaces any given source locations,
    /// and embeds them in the IR tree.
    /// </summary>
    public class SourceValue : IValue
    {
        public SourceValue(IValue Value, SourceLocation Location)
        {
            this.Value = Value;
            this.Location = Location;
        }

        public IValue Value { get; private set; }
        public SourceLocation Location { get; private set; }

        /// <inheritdoc/>
        public IType Type { get { return Value.Type; } }

        /// <inheritdoc/>
        public ResultOrError<IExpression, LogEntry> CreateGetExpression(
            ILocalScope Scope, SourceLocation Location)
        {
            return Value.CreateGetExpression(Scope, this.Location)
                .MapResult(expr => SourceExpression.Create(expr, this.Location));
        }

        /// <inheritdoc/>
        public ResultOrError<IExpression, LogEntry> CreateAddressOfExpression(
            ILocalScope Scope, SourceLocation Location)
        {
            return Value.CreateAddressOfExpression(Scope, this.Location)
                .MapResult(expr => SourceExpression.Create(expr, this.Location));
        }

        /// <inheritdoc/>
        public ResultOrError<IStatement, LogEntry> CreateSetStatement(
            IExpression Value, ILocalScope Scope, 
            SourceLocation Location)
        {
            return this.Value.CreateSetStatement(Value, Scope, this.Location)
                .MapResult(stmt => SourceStatement.Create(stmt, this.Location));
        }
    }
}

