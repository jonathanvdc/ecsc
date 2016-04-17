using System;
using Flame.Compiler;
using Flame.Compiler.Expressions;
using Flame.Compiler.Statements;

namespace Flame.Ecs
{
	public static class ExpressionConverters
	{
		public static IExpression LookupUnqualifiedName(string Name, ILocalScope Scope)
		{
			var local = Scope.GetVariable(Name);
			if (local != null)
			{
				return local.CreateGetExpression();
			}

			return null;
		}

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
	}
}

