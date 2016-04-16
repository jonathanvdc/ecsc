using Flame;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Flame.Ecs
{
	/// <summary>
	/// An interface for namespaces that support inserting 
	/// additional types and nested namespaces.
	/// </summary>
	public interface IMutableNamespace : INamespaceBranch
	{
		void AddType(IType Type);
		void AddNamespace(INamespaceBranch Namespace);
	}

	/// <summary>
	/// A base class for mutable namespaces.
	/// </summary>
	public abstract class MutableNamespaceBase : IMutableNamespace
	{
		public MutableNamespaceBase()
		{
			this.types = new List<IType>();
			this.nsBranches = new List<INamespaceBranch>();
		}

		public abstract IAssembly DeclaringAssembly { get; }
		public abstract string FullName { get; }
		public abstract IEnumerable<IAttribute> Attributes { get; }
		public abstract string Name { get; }

		private List<IType> types;
		private List<INamespaceBranch> nsBranches;

		public IEnumerable<IType> Types { get { return types; } }
		public IEnumerable<INamespaceBranch> Namespaces { get { return nsBranches; } }

		public void AddType(IType Type)
		{
			types.Add(Type);
		}

		public void AddNamespace(INamespaceBranch Namespace)
		{
			nsBranches.Add(Namespace);
		}
	}

	public class RootNamespace : MutableNamespaceBase
	{
		public RootNamespace(IAssembly DeclaringAssembly)
		{
			this.asm = DeclaringAssembly;
		}

		private IAssembly asm;

		public override IAssembly DeclaringAssembly { get { return asm; } }

		public override string FullName
		{
			get { return Name; }
		}

		public override IEnumerable<IAttribute> Attributes
		{
			get { return Enumerable.Empty<IAttribute>(); }
		}

		public override string Name
		{
			get { return ""; }
		}
	}
}
