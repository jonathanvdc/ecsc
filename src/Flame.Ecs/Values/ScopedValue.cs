using System;
using Flame.Compiler;
using Flame.Compiler.Expressions;
using Flame.Compiler.Statements;

namespace Flame.Ecs.Values
{
    /// <summary>
    /// A value that owns a scope, and releases the variables in
    /// that scope when 
    /// </summary>
    public class ScopedValue : IValue
    {
        public ScopedValue(IValue Value, LocalScope Scope)
        {
            this.Value = Value;
            this.Scope = Scope;
        }

        /// <summary>
        /// Gets the value that owns a scope.
        /// </summary>
        /// <value>The value.</value>
        public IValue Value { get; private set; }

        /// <summary>
        /// Gets the local scope that is owned by the value.
        /// </summary>
        /// <value>The local scope.</value>
        public LocalScope Scope { get; private set; }

        /// <inheritdoc/>
        public IType Type { get { return Value.Type; } }

        /// <inheritdoc/>
        public ResultOrError<IExpression, LogEntry> CreateGetExpression(
            ILocalScope Scope, SourceLocation Location)
        {
            return Value.CreateGetExpression(Scope, Location)
                .MapResult<IExpression>(expr =>
                    new InitializedExpression(
                        EmptyStatement.Instance,
                        expr, this.Scope.Release()));
        }

        /// <inheritdoc/>
        public ResultOrError<IExpression, LogEntry> CreateAddressOfExpression(
            ILocalScope Scope, SourceLocation Location)
        {
            return Value.CreateAddressOfExpression(Scope, Location)
                .MapResult<IExpression>(expr =>
                    new InitializedExpression(
                        EmptyStatement.Instance,
                        expr, this.Scope.Release()));
        }

        /// <inheritdoc/>
        public ResultOrError<IStatement, LogEntry> CreateSetStatement(
            IExpression Value, ILocalScope Scope, 
            SourceLocation Location)
        {
            return this.Value.CreateSetStatement(Value, Scope, Location)
                .MapResult<IStatement>(stmt =>
                    new BlockStatement(new IStatement[]
                    {
                        stmt, 
                        this.Scope.Release()
                    }));
        }
    }
}

