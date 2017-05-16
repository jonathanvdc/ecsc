using System;
using System.Collections.Generic;
using System.Linq;
using Flame.Compiler;
using Flame.Compiler.Expressions;
using Flame.Compiler.Statements;
using Flame.Compiler.Variables;

namespace Flame.Ecs.Values
{
    /// <summary>
    /// A value that represents an indexer access expression.
    /// </summary>
    public class IndexerValue : IValue
    {
        public IndexerValue(
            IProperty Property,
            IExpression Target,
            IEnumerable<IExpression> IndexerArguments)
        {
            this.Property = Property;
            this.Target = Target;
            this.IndexerArguments = IndexerArguments;
        }

        /// <summary>
        /// Gets the property that is accessed.
        /// </summary>
        /// <value>The property.</value>
        public IProperty Property { get; private set; }

        /// <summary>
        /// Gets the expression on which the property is accessed.
        /// </summary>
        /// <value>The target.</value>
        public IExpression Target { get; private set; }

        /// <summary>
        /// Gets the sequence of indexer argument expressions for the property.
        /// </summary>
        /// <returns>Indexer arguments.</returns>
        public IEnumerable<IExpression> IndexerArguments { get; private set; }

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
                return ResultOrError<IExpression, LogEntry>.FromResult(
                    new InvocationExpression(getAccessor, Target, IndexerArguments));
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
                return ResultOrError<IStatement, LogEntry>.FromResult(
                    new ExpressionStatement(
                        new InvocationExpression(
                            setAccessor, Target,
                            IndexerArguments.Concat(new IExpression[] { Value }).ToArray())));
            }
        }
    }
}

