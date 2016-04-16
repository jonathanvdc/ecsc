using System;
using Flame.Compiler;
using Flame.Compiler.Expressions;

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
	}
}

