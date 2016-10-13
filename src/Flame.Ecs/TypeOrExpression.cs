using System;
using Flame.Compiler;
using Flame.Compiler.Expressions;
using System.Collections.Generic;
using System.Linq;

namespace Flame.Ecs
{
    /// <summary>
    /// A data structure that represents an entity that
    /// is an expression, a type, a namespace, or a
    /// combination of these things.
    /// </summary>
    public sealed class TypeOrExpression
    {
        public TypeOrExpression(IExpression Expression)
            : this(Expression, Enumerable.Empty<IType>(), default(QualifiedName))
        {
        }

        public TypeOrExpression(IExpression Expression, IEnumerable<IType> Types)
            : this(Expression, Types, default(QualifiedName))
        {
        }

        public TypeOrExpression(IEnumerable<IType> Types)
            : this(null, Types, default(QualifiedName))
        {
        }

        public TypeOrExpression(IExpression Expression, QualifiedName Namespace)
            : this(Expression, Enumerable.Empty<IType>(), Namespace)
        {
        }

        public TypeOrExpression(QualifiedName Namespace)
            : this(null, Enumerable.Empty<IType>(), Namespace)
        {
        }

        public TypeOrExpression(IExpression Expression, IEnumerable<IType> Types, QualifiedName Namespace)
        {
            this.Expression = Expression;
            this.typeSet = new HashSet<IType>(Types);
            this.Namespace = Namespace;
        }

        private HashSet<IType> typeSet;

        public IExpression Expression { get; private set; }

        public IEnumerable<IType> Types { get { return typeSet; } }

        public QualifiedName Namespace { get; private set; }

        public bool IsExpression { get { return Expression != null; } }

        public bool IsType { get { return typeSet.Count > 0; } }

        public bool IsNamespace { get { return !Namespace.IsEmpty; } }

        public IType ExpressionType
        {
            get
            {
                return Expression == null
                    ? null
                    : Expression.Type;
            }
        }

        /// <summary>
        /// Adds a source location to this type-or-expression object.
        /// A new instance is returned that represents the updated entity.
        /// </summary>
        public TypeOrExpression WithSourceLocation(SourceLocation Location)
        {
            if (Expression == null)
                return this;
            else
                return new TypeOrExpression(
                    SourceExpression.Create(Expression, Location), Types, Namespace);
        }

        /// <summary>
        /// Tries to collapse this type-or-expression data structure's
        /// types into a single type. If this instance does not
        /// represent any type, then null is returned. 
        /// If this instance represents a single type, then that
        /// type is returned. 
        /// Otherwise, an error is logged, and one of the 
        /// possible types is returned.
        /// </summary>
        public IType CollapseTypes(SourceLocation Location, GlobalScope Scope)
        {
            if (typeSet.Count == 0)
            {
                return null;
            }
            else if (typeSet.Count == 1)
            {
                return typeSet.First();
            }
            else
            {
                var result = typeSet.First();

                Scope.Log.LogError(new LogEntry(
                    "ambiguous type",
                    NodeHelpers.HighlightEven(
                        "there are ", typeSet.Count.ToString(), 
                        " types named '", result.FullName.ToString(), "'."),
                    Location));

                return result;
            }
        }

        /// <summary>
        /// Gets the empty type-or-expression: an entity that is neither
        /// an expression, nor a type, nor a namespace.
        /// </summary>
        public static readonly TypeOrExpression Empty = 
            new TypeOrExpression(null, Enumerable.Empty<IType>(), default(QualifiedName));


        /// <summary>
        /// Gets the error type-or-expression: an entity that is both
        /// the error type and the error expression.
        /// </summary>
        public static readonly TypeOrExpression Error = 
            new TypeOrExpression(
                ExpressionConverters.ErrorTypeExpression, 
                new IType[] { ErrorType.Instance }, 
                default(QualifiedName));
    }
}

