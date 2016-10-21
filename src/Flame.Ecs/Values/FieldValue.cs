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
                .BindResult(targetExpr =>
                {
                    if (Field.GetIsConstant())
                    {
                        return ResultOrError<IExpression, LogEntry>.FromError(
                            new LogEntry(
                                "constant field",
                                NodeHelpers.HighlightEven(
                                    "field '", Field.Name.ToString(), "' is '", "const", "'; " +
                                    "its address cannot be taken."),
                                Location));
                    }
                    return ResultOrError<IExpression, LogEntry>.FromResult(
                        new FieldGetPointerExpression(Field, targetExpr));
                });
        }

        /// <inheritdoc/>
        public ResultOrError<IStatement, LogEntry> CreateSetStatement(
            IExpression Value, ILocalScope Scope, 
            SourceLocation Location)
        {
            return ExpressionConverters.AsTargetValue(
                Target, Scope, Location, false)
                .BindResult(targetExpr =>
                {
                    if (Field.HasAttribute(
                        PrimitiveAttributes.Instance.InitOnlyAttribute.AttributeType))
                    {
                        // Values can be assigned to 'readonly' fields in exactly two 
                        // places:
                        //    1) the constructors of the class that defines the field,
                        //       with the same staticness as the field
                        //    2) the field's initial value
                        //
                        // Only the former case is relevant here; the latter case is handled
                        // separately during field definition analysis.

                        var method = Scope.Function.CurrentMethod;
                        if (!method.IsConstructor 
                            || method.IsStatic != Field.IsStatic
                            || !Field.DeclaringType.Equals(Scope.Function.DeclaringType))
                        {
                            string staticOrInst = Field.IsStatic ? "static" : "instance";
                            return ResultOrError<IStatement, LogEntry>.FromError(
                                new LogEntry(
                                    "readonly field",
                                    NodeHelpers.HighlightEven(
                                        staticOrInst + " field '", Field.Name.ToString(), "' is '", "readonly", 
                                        "' and can only be assigned a value in a " + staticOrInst +
                                        " constructor or a field initializer."),
                                    Location));
                        }
                    }
                    if (Field.GetIsConstant())
                    {
                        // No value can ever be assigned to a 'const' field (outside
                        // of its definition).

                        return ResultOrError<IStatement, LogEntry>.FromError(
                            new LogEntry(
                                "constant field",
                                NodeHelpers.HighlightEven(
                                    "field '", Field.Name.ToString(), "' is '", "const", "' and cannot " +
                                    "be assigned a value."),
                                Location));
                    }
                    return ResultOrError<IStatement, LogEntry>.FromResult(
                        new FieldSetStatement(Field, targetExpr, Value));
                });
        }
    }
}

