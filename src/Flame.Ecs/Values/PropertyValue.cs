using System;
using Flame.Compiler;
using Flame.Compiler.Expressions;
using Flame.Compiler.Statements;
using Flame.Compiler.Variables;

namespace Flame.Ecs.Values
{
    /// <summary>
    /// A value that represents a property member access expression.
    /// </summary>
    public class PropertyValue : IValue
    {
        public PropertyValue(IProperty Property, IValue Target)
        {
            this.Property = Property;
            this.Target = Target;
        }

        /// <summary>
        /// Gets the property that is accessed.
        /// </summary>
        /// <value>The property.</value>
        public IProperty Property { get; private set; }

        /// <summary>
        /// Gets the expression on which the field is
        /// accessed.
        /// </summary>
        /// <value>The target.</value>
        public IValue Target { get; private set; }

        /// <inheritdoc/>
        public IType Type { get { return Property.PropertyType; } }

        /// <inheritdoc/>
        public ResultOrError<IExpression, LogEntry> CreateGetExpression(
            ILocalScope Scope, SourceLocation Location)
        {
            var getAccessor = Property.GetGetAccessor();
            if (getAccessor == null)
            {
                return ResultOrError<IExpression, LogEntry>.FromError(
                    new LogEntry(
                        "missing getter",
                        NodeHelpers.HighlightEven(
                            "cannot get the value of property '", Property.Name.ToString(), 
                            "' because it does not define a '", "get", "' accessor."),
                        Location));
            }
            else if (!Scope.Function.CanAccess(getAccessor))
            {
                return ResultOrError<IExpression, LogEntry>.FromError(
                    new LogEntry(
                        "inaccessible getter",
                        NodeHelpers.HighlightEven(
                            "cannot get the value of property '", Property.Name.ToString(), 
                            "' because its '", "get", "' accessor is not accessible in this scope."),
                        Location));
            }
            else
            {
                return ExpressionConverters.AsTargetValue(
                    Target, Property.DeclaringType, Scope, Location, true)
                    .MapResult<IExpression>(targetExpr =>
                    {
                        return new InvocationExpression(getAccessor, targetExpr, new IsExpression[] { });
                    });
            }
        }

        /// <inheritdoc/>
        public ResultOrError<IExpression, LogEntry> CreateAddressOfExpression(
            ILocalScope Scope, SourceLocation Location)
        {
            return ResultOrError<IExpression, LogEntry>.FromError(
                new LogEntry(
                    "invalid operation",
                    NodeHelpers.HighlightEven(
                        "cannot get the address of property '", Property.Name.ToString(), 
                        "' because properties do not have addresses."),
                    Location));
        }

        /// <inheritdoc/>
        public ResultOrError<IStatement, LogEntry> CreateSetStatement(
            IExpression Value, ILocalScope Scope, 
            SourceLocation Location)
        {
            var setAccessor = Property.GetSetAccessor();
            if (setAccessor == null)
            {
                return ResultOrError<IStatement, LogEntry>.FromError(
                    new LogEntry(
                        "missing getter",
                        NodeHelpers.HighlightEven(
                            "cannot set the value of property '", Property.Name.ToString(), 
                            "' because it does not define a '", "set", "' accessor."),
                        Location));
            }
            else if (!Scope.Function.CanAccess(setAccessor))
            {
                return ResultOrError<IStatement, LogEntry>.FromError(
                    new LogEntry(
                        "inaccessible setter",
                        NodeHelpers.HighlightEven(
                            "cannot set the value of property '", Property.Name.ToString(), 
                            "' because its '", "set", "' accessor is not accessible in this scope."),
                        Location));
            }
            else
            {
                return ExpressionConverters.AsTargetValue(
                    Target, Property.DeclaringType, Scope, Location, true)
                        .MapResult<IStatement>(targetExpr =>
                            {
                                return new ExpressionStatement(
                                    new InvocationExpression(
                                        setAccessor, targetExpr, 
                                        new IExpression[] { Value }));
                            });
            }
        }
    }
}

