using Flame;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Flame.Ecs
{
	public class RootNamespace : INamespaceBranch
	{
		public RootNamespace(IAssembly DeclaringAssembly, IEnumerable<INamespaceBranch> Namespaces)
		{
			this.DeclaringAssembly = DeclaringAssembly;
			this.Namespaces = Namespaces;
		}

		public IAssembly DeclaringAssembly { get; private set; }
		public IEnumerable<INamespaceBranch> Namespaces { get; private set; }

		public string FullName
		{
			get { return Name; }
		}

		public IEnumerable<IAttribute> Attributes
		{
			get { return Enumerable.Empty<IAttribute>(); }
		}

		public string Name
		{
			get { return ""; }
		}

		public IEnumerable<IType> Types
		{
			get { return Enumerable.Empty<IType>(); }
		}
	}
}
