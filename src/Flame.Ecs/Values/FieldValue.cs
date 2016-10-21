using System;
using Flame.Compiler;
using Flame.Compiler.Expressions;
using Flame.Compiler.Statements;

namespace Flame.Ecs.Values
{
    /// <summary>
    /// A value that represents a field member access expression.
    /// </summary>
    public class FieldValue : IValue
    {
        public FieldValue(IField Field, IValue Target)
        {
            this.Field = Field;
            this.Target = Target;
        }

        /// <summary>
        /// Gets the field that is accessed.
        /// </summary>
        /// <value>The field.</value>
        public IField Field { get; private set; }

        /// <summary>
        /// Gets the expression on which the field is
        /// accessed.
        /// </summary>
        /// <value>The target.</value>
        public IValue Target { get; private set; }

        /// <inheritdoc/>
        public IType Type { get { return Field.FieldType; } }

        /// <inheritdoc/>
        public ResultOrError<IExpression, LogEntry> CreateGetExpression(
            ILocalScope Scope, SourceLocation Location)
        {
            return ExpressionConverters.AsTargetValue(
                Target, Scope, Location, true)
                .MapResult<IExpression>(targetExpr =>
                {
                    return new FieldGetExpression(Field, targetExpr);
                });
        }

        /// <inheritdoc/>
        public ResultOrError<IExpression, LogEntry> CreateAddressOfExpression(
            ILocalScope Scope, SourceLocation Location)
        {
            return ExpressionConverters.AsTargetValue(
                Target, Scope, Location, false)
                .MapResult<IExpression>(targetExpr =>
                {
                    return new FieldGetPointerExpression(Field, targetExpr);
                });
        }

        /// <inheritdoc/>
        public ResultOrError<IStatement, LogEntry> CreateSetStatement(
            IExpression Value, ILocalScope Scope, 
            SourceLocation Location)
        {
            return ExpressionConverters.AsTargetValue(
                Target, Scope, Location, false)
                .MapResult<IStatement>(targetExpr =>
                {
                    return new FieldSetStatement(Field, targetExpr, Value);
                });
        }
    }
}

