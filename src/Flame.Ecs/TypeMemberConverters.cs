using System;
using Loyc.Syntax;
using Flame.Build;

namespace Flame.Ecs
{
	public static class TypeMemberConverters
	{
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

