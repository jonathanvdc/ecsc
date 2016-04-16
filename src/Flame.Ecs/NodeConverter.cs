using System;
using System.Collections.Generic;
using Loyc.Syntax;

namespace Flame.Ecs
{
	public class NodeConverter
	{
		public NodeConverter()
		{
		}

		public static NodeConverter DefaultNodeConverter
		{
			get
			{
				return new NodeConverter();
			}
		}

		public INamespaceBranch ConvertCompilationUnit(GlobalScope Scope, IAssembly DeclaringAssembly, IEnumerable<LNode> Nodes)
		{
			throw new NotImplementedException();
		}
	}
}

