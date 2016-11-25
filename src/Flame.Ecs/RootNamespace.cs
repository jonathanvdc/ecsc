using Flame;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Flame.Build;
using Flame.Build.Lazy;
using System.Collections.Concurrent;

namespace Flame.Ecs
{
    /// <summary>
    /// An interface for namespaces that support inserting
    /// additional types and nested namespaces.
    /// </summary>
    public interface IMutableNamespace : IMember
    {
        IType DefineType(
            UnqualifiedName Name, Action<LazyDescribedType> AnalyzeBody);

        /// <summary>
        /// Defines a child mutable namespace of this namespace.
        /// If a child namespace with the given namespace already
        /// exists, then that namespace is returned.
        /// </summary>
        /// <returns>The child namespace.</returns>
        /// <param name="Name">The child namespace's name.</param>
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
            this.nsBranches = new ConcurrentDictionary<string, ChildNamespace>();
        }

        public abstract IAssembly DeclaringAssembly { get; }

        public abstract QualifiedName FullName { get; }

        public abstract AttributeMap Attributes { get; }

        public abstract UnqualifiedName Name { get; }

        private List<IType> types;
        private ConcurrentDictionary<string, ChildNamespace> nsBranches;

        public IEnumerable<IType> Types { get { return types; } }

        public IEnumerable<INamespaceBranch> Namespaces { get { return nsBranches.Values; } }

        public IType DefineType(
            UnqualifiedName Name, Action<LazyDescribedType> AnalyzeBody)
        {
            var result = new LazyDescribedType(Name, this, AnalyzeBody);
            types.Add(result);
            return result;
        }

        public IMutableNamespace DefineNamespace(string Name)
        {
            return nsBranches.GetOrAdd(
                Name, name => new ChildNamespace(new SimpleName(name), this));
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

        public override QualifiedName FullName
        {
            get { return default(QualifiedName); }
        }

        public override AttributeMap Attributes
        {
            get { return AttributeMap.Empty; }
        }

        public override UnqualifiedName Name
        {
            get { return new SimpleName(""); }
        }
    }

    public sealed class ChildNamespace : MutableNamespaceBase
    {
        public ChildNamespace(UnqualifiedName Name, INamespace DeclaringNamespace)
        {
            this.DeclaringNamespace = DeclaringNamespace;
            this.nsName = Name;
        }

        public INamespace DeclaringNamespace { get; private set; }

        private UnqualifiedName nsName;

        public override IAssembly DeclaringAssembly { get { return DeclaringNamespace.DeclaringAssembly; } }

        public override QualifiedName FullName
        {
            get { return Name.Qualify(DeclaringNamespace.FullName); }
        }

        public override AttributeMap Attributes
        {
            get { return AttributeMap.Empty; }
        }

        public override UnqualifiedName Name
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

        public UnqualifiedName Name { get { return Type.Name; } }

        public QualifiedName FullName { get { return Type.FullName; } }

        public AttributeMap Attributes { get { return Type.Attributes; } }

        public IType DefineType(
            UnqualifiedName Name, Action<LazyDescribedType> AnalyzeBody)
        {
            var result = new LazyDescribedType(Name, Type, AnalyzeBody);
            Type.AddNestedType(result);
            return result;
        }

        public IMutableNamespace DefineNamespace(string Name)
        {
            return new TypeNamespace((LazyDescribedType)DefineType(
                new SimpleName(Name), _ =>
            {
            }));
        }
    }
}
