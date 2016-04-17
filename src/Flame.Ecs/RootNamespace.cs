using Flame;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Flame.Build;

namespace Flame.Ecs
{
	/// <summary>
	/// An interface for namespaces that support inserting 
	/// additional types and nested namespaces.
	/// </summary>
	public interface IMutableNamespace
	{
		IType DefineType(
			string Name, Action<LazyDescribedType> AnalyzeBody);
		IMutableNamespace DefineNamespace(string Name);
	}

	/// <summary>
	/// A base class for mutable namespaces.
	/// </summary>
	public abstract class MutableNamespaceBase : IMutableNamespace, INamespaceBranch
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

		public IType DefineType(
			string Name, Action<LazyDescribedType> AnalyzeBody)
		{
			var result = new LazyDescribedType(Name, this, AnalyzeBody);
			AddType(result);
			return result;
		}

		public IMutableNamespace DefineNamespace(string Name)
		{
			var result = new ChildNamespace(Name, this);
			AddNamespace(result);
			return result;
		}
	}

	public sealed class RootNamespace : MutableNamespaceBase
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

	public sealed class ChildNamespace : MutableNamespaceBase
	{
		public ChildNamespace(string Name, INamespace DeclaringNamespace)
		{
			this.DeclaringNamespace = DeclaringNamespace;
			this.nsName = Name;
		}

		public INamespace DeclaringNamespace { get; private set; }
		private string nsName;

		public override IAssembly DeclaringAssembly { get { return DeclaringAssembly; } }

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
			get { return nsName; }
		}
	}

	public sealed class TypeNamespace : IMutableNamespace
	{
		public TypeNamespace(LazyDescribedType Type)
		{
			this.Type = Type;
		}

		public LazyDescribedType Type { get; private set; }


		public IType DefineType(
			string Name, Action<LazyDescribedType> AnalyzeBody)
		{
			var result = new LazyDescribedType(Name, Type, AnalyzeBody);
			Type.AddNestedType(result);
			return result;
		}

		public IMutableNamespace DefineNamespace(string Name)
		{
			return new TypeNamespace((LazyDescribedType)DefineType(
				Name, _ => { }));
		}
	}
}
