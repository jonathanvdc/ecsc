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
        /// <summary>
        /// Defines a type with the given name. A sequence of callbacks
        /// is used to analyze the type's body.
        /// </summary>
        /// <returns>The defined type.</returns>
        /// <param name="Name">The name of the type to define.</param>
        /// <param name="BodyAnalysisPhases">
        /// A list of callbacks that analyze the type's body. The initial
        /// argument list consists of the constructed type followed by a boolean
        /// that tells if the current definition is a redefinition. Successive
        /// callbacks get the constructed type and the previous callback's 
        /// return value as arguments.
        /// </param>
        /// <remarks>
        /// Code that defines a type with the same name more than once must
        /// make sure to define all versions of said type before querying
        /// the type's properties or methods.
        /// </remarks>
        IType DefineType(
            UnqualifiedName Name, 
            IReadOnlyList<Func<LazyDescribedType, object, object>> BodyAnalysisPhases);

        /// <summary>
        /// Defines a child mutable namespace of this namespace.
        /// If a child namespace with the given namespace already
        /// exists, then that namespace is returned.
        /// </summary>
        /// <returns>The child namespace.</returns>
        /// <param name="Name">The child namespace's name.</param>
        IMutableNamespace DefineNamespace(string Name);
    }

    public static class MutableNamespaceExtensions
    {
        /// <summary>
        /// Defines a type with the given name. A callback is used to analyze 
        /// the type's body. If a type with the given name already exists, 
        /// then the boolean parameter to the callback is set to true; 
        /// otherwise it is false.
        /// </summary>
        /// <returns>The defined type.</returns>
        /// <param name="Name">The name of the type to define.</param>
        /// <param name="AnalyzeBody">
        /// A callback that analyzes the type's body. The
        /// argument list consists of the constructed type followed by a boolean
        /// that tells if the current definition is a redefinition.
        /// </param>
        /// <remarks>
        /// Code that defines a type with the same name more than once must
        /// make sure to define all versions of said type before querying
        /// the type's properties or methods.
        /// </remarks>
        public static IType DefineType(
            this IMutableNamespace Namespace,
            UnqualifiedName Name, 
            Action<LazyDescribedType, bool> AnalyzeBody)
        {
            return Namespace.DefineType(Name, 
                new Func<LazyDescribedType, object, object>[]
                {
                    (descTy, isRedef) => 
                    {
                        AnalyzeBody(descTy, (bool)isRedef);
                        return null;
                    }
                });
        }

        /// <summary>
        /// Defines a type with the given name.
        /// </summary>
        /// <returns>The defined type.</returns>
        /// <param name="Name">The name of the type to define.</param>
        /// <param name="PhaseOne">
        /// A callback that analyzes the type's body. The
        /// argument list consists of the constructed type followed by a boolean
        /// that tells if the current definition is a redefinition.
        /// </param>
        /// <param name="PhaseTwo">
        /// A callback that performs the second body analysis phase.
        /// </param>
        /// <param name="PhaseThree">
        /// A callback that performs the third body analysis phase.
        /// </param>
        /// <remarks>
        /// Code that defines a type with the same name more than once must
        /// make sure to define all versions of said type before querying
        /// the type's properties or methods.
        /// </remarks>
        public static IType DefineType<T1, T2>(
            this IMutableNamespace Namespace,
            UnqualifiedName Name, 
            Func<LazyDescribedType, bool, T1> PhaseOne,
            Func<LazyDescribedType, T1, T2> PhaseTwo,
            Action<LazyDescribedType, T2> PhaseThree)
        {
            return Namespace.DefineType(Name, 
                new Func<LazyDescribedType, object, object>[]
                {
                    (descTy, isRedef) => PhaseOne(descTy, (bool)isRedef),
                    (descTy, prevResult) => PhaseTwo(descTy, (T1)prevResult),
                    (descTy, prevResult) =>
                    {
                        PhaseThree(descTy, (T2)prevResult);
                        return null;
                    }
                });
        }
    }

    /// <summary>
    /// A data structure that helps with the creation, initialization and 
    /// retrieval of (possibly) partial types.
    /// </summary>
    public sealed class PartialTypeManager
    {
        public PartialTypeManager(
            Func<UnqualifiedName, Action<LazyDescribedType>, LazyDescribedType> CreateType)
        {
            this.createType = CreateType;
            this.types = new Dictionary<UnqualifiedName, LazyDescribedType>();
            this.typeInitPhases = 
                new Dictionary<LazyDescribedType, List<List<Func<LazyDescribedType, object, object>>>>();
        }

        private Dictionary<UnqualifiedName, LazyDescribedType> types;
        private Dictionary<LazyDescribedType, List<List<Func<LazyDescribedType, object, object>>>> typeInitPhases;
        private Func<UnqualifiedName, Action<LazyDescribedType>, LazyDescribedType> createType;

        /// <summary>
        /// Gets the set of all types defined by this partial type manager.
        /// </summary>
        /// <value>The types.</value>
        public IEnumerable<IType> Types { get { return types.Values; } }

        /// <summary>
        /// Initializes the given type.
        /// </summary>
        /// <param name="Type">The type to initialize.</param>
        private void InitializeType(LazyDescribedType Type)
        {
            List<List<Func<LazyDescribedType, object, object>>> initPhases;
            lock (typeInitPhases)
            {
                initPhases = typeInitPhases[Type];
                typeInitPhases[Type] = null;
            }

            var results = new List<object>();
            // Original definition
            results.Add(false);
            for (int i = 1; i < initPhases.Count; i++)
                // Redefinitions
                results.Add(true);

            int maxPhases = initPhases.Max(init => init.Count);
            for (int j = 0; j < maxPhases; j++)
            {
                for (int i = 0; i < initPhases.Count; i++)
                {
                    results[i] = initPhases[i][j](Type, results[i]);
                }
            }
        }

        /// <summary>
        /// Defines a type with the given name. A sequence of callbacks
        /// is used to analyze the type's body.
        /// </summary>
        /// <returns>The defined type.</returns>
        /// <param name="Name">The name of the type to define.</param>
        /// <param name="BodyAnalysisPhases">
        /// A list of callbacks that analyze the type's body. The initial
        /// argument list consists of the constructed type followed by a boolean
        /// that tells if the current definition is a redefinition. Successive
        /// callbacks get the constructed type and the previous callback's 
        /// return value as arguments.
        /// </param>
        /// <remarks>
        /// Code that defines a type with the same name more than once must
        /// make sure to define all versions of said type before querying
        /// the type's properties or methods.
        /// </remarks>
        public IType DefineType(
            UnqualifiedName Name, 
            IReadOnlyList<Func<LazyDescribedType, object, object>> BodyAnalysisPhases)
        {
            // Use a lock, because types and typeInitActions
            // need to be in sync.
            lock (types)
            {
                LazyDescribedType result;
                if (!types.TryGetValue(Name, out result))
                {
                    result = createType(Name, InitializeType);
                    types[Name] = result;
                    typeInitPhases[result] = new List<List<Func<LazyDescribedType, object, object>>>();
                }
                typeInitPhases[result].Add(BodyAnalysisPhases.ToList());
                return result;
            }
        }
    }

    /// <summary>
    /// A base class for mutable namespaces.
    /// </summary>
    public abstract class MutableNamespaceBase : IMutableNamespace, INamespaceBranch
    {
        public MutableNamespaceBase()
        {
            this.typeManager = new PartialTypeManager(CreateType);
            this.nsBranches = new ConcurrentDictionary<string, ChildNamespace>();
        }

        public abstract IAssembly DeclaringAssembly { get; }

        public abstract QualifiedName FullName { get; }

        public abstract AttributeMap Attributes { get; }

        public abstract UnqualifiedName Name { get; }

        private PartialTypeManager typeManager;
        private ConcurrentDictionary<string, ChildNamespace> nsBranches;

        public IEnumerable<IType> Types { get { return typeManager.Types; } }

        public IEnumerable<INamespaceBranch> Namespaces { get { return nsBranches.Values; } }

        private LazyDescribedType CreateType(
            UnqualifiedName Name, 
            Action<LazyDescribedType> AnalyzeBody)
        {
            return new LazyDescribedType(Name, this, AnalyzeBody);
        }

        /// <inheritdoc/>
        public IType DefineType(
            UnqualifiedName Name, 
            IReadOnlyList<Func<LazyDescribedType, object, object>> BodyAnalysisPhases)
        {
            return typeManager.DefineType(Name, BodyAnalysisPhases);
        }

        /// <inheritdoc/>
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
            this.typeManager = new PartialTypeManager(CreateType);
        }

        public LazyDescribedType Type { get; private set; }

        public UnqualifiedName Name { get { return Type.Name; } }

        public QualifiedName FullName { get { return Type.FullName; } }

        public AttributeMap Attributes { get { return Type.Attributes; } }

        private PartialTypeManager typeManager;

        private LazyDescribedType CreateType(
            UnqualifiedName Name, 
            Action<LazyDescribedType> AnalyzeBody)
        {
            var result = new LazyDescribedType(Name, Type, AnalyzeBody);
            Type.AddNestedType(result);
            return result;
        }

        /// <inheritdoc/>
        public IType DefineType(
            UnqualifiedName Name, 
            IReadOnlyList<Func<LazyDescribedType, object, object>> BodyAnalysisPhases)
        {
            return typeManager.DefineType(Name, BodyAnalysisPhases);
        }

        /// <inheritdoc/>
        public IMutableNamespace DefineNamespace(string Name)
        {
            return new TypeNamespace((LazyDescribedType)this.DefineType(
                new SimpleName(Name), (descTy, isRedef) =>
            {
            }));
        }
    }
}
