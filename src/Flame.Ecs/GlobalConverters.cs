using System;
using Loyc.Syntax;

namespace Flame.Ecs
{
	public static class GlobalConverters
	{
		/// <summary>
		/// Converts an '#import' directive.
		/// </summary>
		public static GlobalScope ConvertImportDirective(LNode Node, IMutableNamespace Namespace, GlobalScope Scope)
		{
			if (!NodeHelpers.CheckArity(Node, 1, Scope.Log))
				return Scope;
			
			var qualName = NodeHelpers.ToQualifiedName(Node.Args[0]);
			return Scope.WithBinder(Scope.Binder.UseNamespace(qualName));
		}
	}
}

