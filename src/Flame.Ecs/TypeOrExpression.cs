using System;
using Flame.Compiler;
using Flame.Compiler.Expressions;

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
			: this(Expression, null, null)
		{ }
		public TypeOrExpression(IExpression Expression, IType Type)
			: this(Expression, Type, null)
		{ }
		public TypeOrExpression(IType Type)
			: this(null, Type, null)
		{ }
		public TypeOrExpression(IExpression Expression, QualifiedName Namespace)
			: this(Expression, null, Namespace)
		{ }
		public TypeOrExpression(QualifiedName Namespace)
			: this(null, null, Namespace)
		{ }
		public TypeOrExpression(IExpression Expression, IType Type, QualifiedName Namespace)
		{
			this.Expression = Expression;
			this.Type = Type;
			this.Namespace = Namespace;
		}

		public IExpression Expression { get; private set; }
		public IType Type { get; private set; }
		public QualifiedName Namespace { get; private set; }

		public bool IsExpression { get { return Expression != null; } }
		public bool IsType { get { return Type != null; } }
		public bool IsNamespace { get { return Namespace != null; } }

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
					SourceExpression.Create(Expression, Location), Type, Namespace);
		}

		/// <summary>
		/// Gets the empty type-or-expression: an entity that is neither
		/// an expression, nor a type, nor a namespace.
		/// </summary>
		public static readonly TypeOrExpression Empty = new TypeOrExpression(null, null, null);
	}
}

