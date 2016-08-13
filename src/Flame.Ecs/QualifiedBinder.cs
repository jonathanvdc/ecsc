using System;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace Flame.Ecs
{
    /// <summary>
    /// A type of object that maps qualified names to types.
    /// </summary>
    public sealed class QualifiedBinder : IBinder
    {
        private QualifiedBinder(
            IBinder Binder, 
            HashSet<QualifiedName> namespaceUsings, 
            Lazy<HashSet<IType>> typeUsings,
            Dictionary<UnqualifiedName, QualifiedName> nameAliases,
            Dictionary<UnqualifiedName, IType> typeAliases)
        {
            this.Binder = Binder;
            this.namespaceUsings = namespaceUsings;
            this.typeUsings = typeUsings;
            this.nameAliases = nameAliases;
            this.typeAliases = typeAliases;
            this.resolvedTypeCache = new ConcurrentDictionary<QualifiedName, IType>();
        }

        public QualifiedBinder(IBinder Binder)
            : this(
                Binder, new HashSet<QualifiedName>(), 
                new Lazy<HashSet<IType>>(() => new HashSet<IType>()),
                new Dictionary<UnqualifiedName, QualifiedName>(),
                new Dictionary<UnqualifiedName, IType>())
        {
        }

        /// <summary>
        /// Gets this qualified name binder's underlying binder.
        /// </summary>
        public IBinder Binder { get; private set; }

        /// <summary>
        /// Gets the environment for this binder.
        /// </summary>
        public IEnvironment Environment { get { return Binder.Environment; } }

        /// <summary>
        /// Gets the set of all types that are used by this qualified binder.
        /// </summary>
        public IEnumerable<IType> TypeUsings { get { return typeUsings.Value; } }

        private HashSet<QualifiedName> namespaceUsings;
        private Lazy<HashSet<IType>> typeUsings;
        private Dictionary<UnqualifiedName, QualifiedName> nameAliases;
        private Dictionary<UnqualifiedName, IType> typeAliases;
        private ConcurrentDictionary<QualifiedName, IType> resolvedTypeCache;

        /// <summary>
        /// Adds the given qualified name to the set of used namespaces.
        /// </summary>
        public QualifiedBinder UseNamespace(QualifiedName Name)
        {
            var newUsings = new HashSet<QualifiedName>(namespaceUsings);
            newUsings.Add(Name);
            return new QualifiedBinder(Binder, newUsings, typeUsings, nameAliases, typeAliases);
        }

        /// <summary>
        /// Adds the given type to the set of used types.
        /// </summary>
        public QualifiedBinder UseType(Lazy<IType> Type)
        {
            var newUsings = new Lazy<HashSet<IType>>(() =>
            {
                var ty = Type.Value;
                if (ty == null)
                {
                    return typeUsings.Value;
                }
                else
                {
                    var oldSet = new HashSet<IType>(typeUsings.Value);
                    oldSet.Add(ty);
                    return oldSet;
                }
            });
            return new QualifiedBinder(Binder, namespaceUsings, newUsings, nameAliases, typeAliases);
        }

        /// <summary>
        /// Creates an alias for a qualified name.
        /// </summary>
        public QualifiedBinder AliasName(UnqualifiedName Alias, QualifiedName Name)
        {
            var newAliases = new Dictionary<UnqualifiedName, QualifiedName>(nameAliases);
            newAliases[Alias] = Name;
            return new QualifiedBinder(Binder, namespaceUsings, typeUsings, newAliases, typeAliases);
        }

        /// <summary>
        /// Creates an alias for the given type.
        /// </summary>
        public QualifiedBinder AliasType(UnqualifiedName Alias, IType Type)
        {
            var tyAliases = new Dictionary<UnqualifiedName, IType>(typeAliases);
            tyAliases[Alias] = Type;
            return new QualifiedBinder(Binder, namespaceUsings, typeUsings, nameAliases, tyAliases);
        }

        public IEnumerable<IType> GetTypes()
        {
            return Binder.GetTypes();
        }

        /// <summary>
        /// Binds the given qualified name to a type.
        /// </summary>
        public IType BindType(QualifiedName Name)
        {
            return resolvedTypeCache.GetOrAdd(Name, BindTypeImpl);
        }

        private IType BindTypeImpl(QualifiedName Name)
        {
            if (Name.IsEmpty)
                return null;

            IType result;
            if (!Name.IsQualified && typeAliases.TryGetValue(Name.Qualifier, out result))
                return result;

            QualifiedName aliasedQualifier;
            if (nameAliases.TryGetValue(Name.Qualifier, out aliasedQualifier))
                Name = Name.Qualify(aliasedQualifier);

            result = Binder.BindType(Name);
            if (result != null)
                return result;

            foreach (var prefix in namespaceUsings)
            {
                result = Binder.BindType(Name.Qualify(prefix));
                if (result != null)
                    return result;
            }

            return null;
        }
    }
}

