using System;
using Loyc.Syntax;
using Flame.Build;
using Flame.Compiler;
using Flame.Compiler.Statements;

namespace Flame.Ecs
{
	public static class TypeMemberConverters
	{
		public static IStatement ToStatement(IExpression Expression)
		{
			return new ExpressionStatement(Expression);
		}

		/// <summary>
		/// Appends a `return(void);` statement to the given function 
		/// body expression, provided the return type is either `null` 
		/// or `void`. Otherwise, the body expression's value is returned,
		/// provided that its return value is not 'void'. 
		/// </summary>
		public static IStatement AutoReturn(IType ReturnType, IExpression Body, SourceLocation Location, GlobalScope Scope)
		{
			if (ReturnType == null || ReturnType.Equals(PrimitiveTypes.Void))
				return new BlockStatement(new[] { ToStatement(Body), new ReturnStatement() });
			else if (!Body.Type.Equals(PrimitiveTypes.Void))
				return new ReturnStatement(Scope.ConvertImplicit(Body, ReturnType, Location));
			else
				return ToStatement(Body);
		}

		/// <summary>
		/// Converts an '#fn' function declaration node.
		/// </summary>
		public static GlobalScope ConvertFunction(
			LNode Node, DescribedType DeclaringType, 
			GlobalScope Scope, NodeConverter Converter)
		{
			throw new NotImplementedException();
		}
	}
}

