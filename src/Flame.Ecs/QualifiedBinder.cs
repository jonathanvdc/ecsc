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
            TypeCache allTypes,
            HashSet<QualifiedName> namespaceUsings, 
            Lazy<HashSet<IType>> typeUsings,
            Dictionary<UnqualifiedName, QualifiedName> nameAliases,
            Dictionary<UnqualifiedName, IType> typeAliases,
            Lazy<Dictionary<UnqualifiedName, IType>> visibleTypeMap,
            Lazy<HashSet<IType>> extTypes)
        {
            this.allTypes = allTypes;
            this.namespaceUsings = namespaceUsings;
            this.typeUsings = typeUsings;
            this.nameAliases = nameAliases;
            this.typeAliases = typeAliases;
            this.visibleTypeMap = visibleTypeMap;
            this.extTypes = extTypes;
            this.qualifiedTypeCache = new ConcurrentDictionary<QualifiedName, IType>();
        }

        private QualifiedBinder(
            TypeCache allTypes,
            HashSet<QualifiedName> namespaceUsings, 
            Lazy<HashSet<IType>> typeUsings,
            Dictionary<UnqualifiedName, QualifiedName> nameAliases,
            Dictionary<UnqualifiedName, IType> typeAliases,
            Lazy<Dictionary<UnqualifiedName, IType>> visibleTypeMap)
            : this(
                allTypes, namespaceUsings, typeUsings,
                nameAliases, typeAliases, visibleTypeMap,
                new Lazy<HashSet<IType>>(() => FindExtensionTypes(visibleTypeMap.Value.Values)))
        { }


        public QualifiedBinder(TypeCache Cache)
            : this(
                Cache,
                new HashSet<QualifiedName>(), 
                new Lazy<HashSet<IType>>(() => new HashSet<IType>()),
                new Dictionary<UnqualifiedName, QualifiedName>(),
                new Dictionary<UnqualifiedName, IType>(),
                new Lazy<Dictionary<UnqualifiedName, IType>>(() => FindTopLevelTypes(Cache)))
        { }

        public QualifiedBinder(IBinder Binder)
            : this(new TypeCache(Binder))
        { }

        /// <summary>
        /// Gets this qualified name binder's underlying binder.
        /// </summary>
        public IBinder Binder { get { return allTypes.Binder; } }

        /// <summary>
        /// Gets the environment for this binder.
        /// </summary>
        public IEnvironment Environment { get { return Binder.Environment; } }

        /// <summary>
        /// Gets the set of all types that are used by this qualified binder.
        /// </summary>
        public IEnumerable<IType> TypeUsings { get { return typeUsings.Value; } }

        /// <summary>
        /// Gets the set of all extension types that are directly visible
        /// to this qualified binder.
        /// </summary>
        public IEnumerable<IType> ExtensionTypes { get { return extTypes.Value; } }

        /// <summary>
        /// Gets the set of all types that are directly visible to this
        /// qualified binder, i.e., they can be accessed by their
        /// unqualified names.
        /// </summary>
        /// <value>The set of all visible types.</value>
        public IEnumerable<IType> VisibleTypes { get { return visibleTypeMap.Value.Values; } }

        private HashSet<QualifiedName> namespaceUsings;
        private Lazy<HashSet<IType>> typeUsings;
        private Dictionary<UnqualifiedName, QualifiedName> nameAliases;
        private Dictionary<UnqualifiedName, IType> typeAliases;
        private Lazy<Dictionary<UnqualifiedName, IType>> visibleTypeMap;
        private ConcurrentDictionary<QualifiedName, IType> qualifiedTypeCache;
        private Lazy<HashSet<IType>> extTypes;
        private TypeCache allTypes;

        /// <summary>
        /// Adds the given qualified name to the set of used namespaces.
        /// </summary>
        public QualifiedBinder UseNamespace(QualifiedName Name)
        {
            var newUsings = new HashSet<QualifiedName>(namespaceUsings);
            if (newUsings.Add(Name))
            {
                return new QualifiedBinder(
                    allTypes, newUsings, typeUsings, nameAliases, typeAliases,
                    MergeVisibleTypes(visibleTypeMap, Name, allTypes),
                    MergeExtensionTypes(extTypes, Name, allTypes));
            }
            else
            {
                return this;
            }
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
            return new QualifiedBinder(
                allTypes, namespaceUsings, newUsings, 
                nameAliases, typeAliases, visibleTypeMap,
                extTypes);
        }

        /// <summary>
        /// Creates an alias for a qualified name.
        /// </summary>
        public QualifiedBinder AliasName(UnqualifiedName Alias, QualifiedName Name)
        {
            var newAliases = new Dictionary<UnqualifiedName, QualifiedName>(nameAliases);
            newAliases[Alias] = Name;
            return new QualifiedBinder(
                allTypes, namespaceUsings, typeUsings, 
                newAliases, typeAliases,
                visibleTypeMap, extTypes);
        }

        /// <summary>
        /// Creates an alias for the given type.
        /// </summary>
        public QualifiedBinder AliasType(UnqualifiedName Alias, IType Type)
        {
            var tyAliases = new Dictionary<UnqualifiedName, IType>(typeAliases);
            tyAliases[Alias] = Type;
            return new QualifiedBinder(
                allTypes, namespaceUsings, typeUsings, 
                nameAliases, tyAliases, visibleTypeMap,
                extTypes);
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
            if (Name.IsEmpty)
                return null;

            if (!Name.IsQualified)
            {
                IType result;
                var qual = Name.Qualifier;
                if (typeAliases.TryGetValue(qual, out result)
                    || visibleTypeMap.Value.TryGetValue(qual, out result))
                    return result;
                else
                    return null;
            }
            else
            {
                return qualifiedTypeCache.GetOrAdd(Name, BindQualifiedTypeImpl);
            }
        }

        private IType BindQualifiedTypeImpl(QualifiedName Name)
        {
            QualifiedName aliasedQualifier;
            if (nameAliases.TryGetValue(Name.Qualifier, out aliasedQualifier))
                Name = Name.Qualify(aliasedQualifier);

            var result = allTypes.LookupType(Name);
            if (result != null)
                return result;

            foreach (var prefix in namespaceUsings)
            {
                result = allTypes.LookupType(Name.Qualify(prefix));
                if (result != null)
                    return result;
            }

            return null;
        }

        private static HashSet<IType> FindExtensionTypes(IEnumerable<IType> Types)
        {
            var results = new HashSet<IType>();
            foreach (var ty in Types)
            {
                if (ty.GetIsExtension())
                {
                    results.Add(ty);
                }
            }
            return results;
        }

        private static Dictionary<UnqualifiedName, IType> FindTopLevelTypes(TypeCache Cache)
        {
            var result = new Dictionary<UnqualifiedName, IType>();
            MergeVisibleTypesImpl(result, default(QualifiedName), Cache);
            return result;
        }

        private static void MergeVisibleTypesImpl(
            Dictionary<UnqualifiedName, IType> VisibleTypes, 
            QualifiedName Namespace,
            TypeCache Cache)
        {
            foreach (var pair in Cache.GetOrCreateTypeMap(Namespace))
            {
                VisibleTypes[pair.Key] = pair.Value;
            }
        }

        private static Lazy<Dictionary<UnqualifiedName, IType>> MergeVisibleTypes(
            Lazy<Dictionary<UnqualifiedName, IType>> VisibleTypes, 
            QualifiedName Namespace,
            TypeCache Cache)
        {
            return new Lazy<Dictionary<UnqualifiedName, IType>>(() =>
            {
                var results = new Dictionary<UnqualifiedName, IType>(VisibleTypes.Value);
                MergeVisibleTypesImpl(results, Namespace, Cache);
                return results;
            });
        }

        private static Lazy<HashSet<IType>> MergeExtensionTypes(
            Lazy<HashSet<IType>> ExtensionTypes, 
            QualifiedName Namespace,
            TypeCache Cache)
        {
            return new Lazy<HashSet<IType>>(() =>
            {
                var results = new HashSet<IType>(ExtensionTypes.Value);
                foreach (var pair in Cache.GetOrCreateTypeMap(Namespace))
                {
                    if (pair.Value.GetIsExtension())
                        results.Add(pair.Value);
                }
                return results;
            });
        }
    }
}

