using System;
using System.Collections.Generic;

namespace Flame.Ecs
{
    using TypeDict = Dictionary<UnqualifiedName, IType>;

    /// <summary>
    /// A data structure that lazily computes and then
    /// stores information related to types, based on
    /// a binder.
    /// </summary>
    public struct TypeCache
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Flame.Ecs.TypeCache"/> struct.
        /// The given binder is used to find types.
        /// </summary>
        /// <param name="Binder">A binder that is used to find types.</param>
        public TypeCache(IBinder Binder)
        {
            this = default(TypeCache);
            this.Binder = Binder;
            this.lazyTypes = new Lazy<Dictionary<QualifiedName, TypeDict>>(
                InitializeDictionaries);
        }

        private Lazy<Dictionary<QualifiedName, TypeDict>> lazyTypes;

        /// <summary>
        /// Gets the binder for this type cache.
        /// </summary>
        public IBinder Binder { get; private set; }

        private Dictionary<QualifiedName, TypeDict> InitializeDictionaries()
        {
            var results = new Dictionary<QualifiedName, TypeDict>();
            foreach (var type in Binder.GetTypes())
            {
                var ns = type.DeclaringNamespace;
                var nsName = ns == null
                    ? default(QualifiedName)
                    : ns.FullName;

                TypeDict tDict;
                if (!results.TryGetValue(nsName, out tDict))
                {
                    tDict = new TypeDict();
                    results[nsName] = tDict;
                }

                tDict[type.Name] = type;
            }
            return results;
        }

        /// <summary>
        /// Tries to get a read-only dictionary that maps simple names
        /// to types, where all types are in the given namespace.
        /// If no type exists that has the given namespace, then 
        /// 'false' is returned.
        /// </summary>
        /// <returns>'true' if at least one type exists with the given namespace, 'false' otherwise.</returns>
        /// <param name="Namespace">The namespace that defines the set of all types that are returned.</param>
        /// <param name="Results">The storage location for a read-only dictionary that maps simple names to types.</param>
        public bool TryGetTypeMap(
            QualifiedName Namespace, 
            out IReadOnlyDictionary<UnqualifiedName, IType> Results)
        {
            TypeDict resultMap;
            bool success = lazyTypes.Value.TryGetValue(Namespace, out resultMap);
            Results = resultMap;
            return success;
        }

        /// <summary>
        /// Tries to get a read-only dictionary that maps simple names
        /// to types, where all types are in the given namespace.
        /// If no type exists that has the given namespace, then 
        /// an empty dictionary is returned.
        /// </summary>
        /// <returns>A read-only dictionary that maps simple names to types.</returns>
        /// <param name="Namespace">The namespace that defines the set of all types that are returned.</param>
        public IReadOnlyDictionary<UnqualifiedName, IType> GetOrCreateTypeMap(
            QualifiedName Namespace)
        {
            IReadOnlyDictionary<UnqualifiedName, IType> result;
            if (!TryGetTypeMap(Namespace, out result))
                result = new TypeDict();
            
            return result;
        }

        /// <summary>
        /// Looks up a type by its full name.
        /// </summary>
        /// <returns>The type.</returns>
        /// <param name="FullName">The type's full name.</param>
        public IType LookupType(
            QualifiedName FullName)
        {
            // Extract the namespace name and the type name
            // from the type's full name.
            //
            // FIXME: replace the 'foreach' loop below
            // by these statements when the Flame version is 
            // updated.
            //
            //     int pLength = name.PathLength;
            //     var nsName = name.Slice(0, pLength);
            //     var name = name[pLength - 1];
            //
            QualifiedName nsName = default(QualifiedName);
            UnqualifiedName name = FullName.Qualifier;
            foreach (var item in FullName.Name.Path)
            {
                nsName = name.Qualify(nsName);
                name = item;
            }

            TypeDict tDict;
            if (lazyTypes.Value.TryGetValue(nsName, out tDict))
            {
                IType result;
                if (tDict.TryGetValue(name, out result))
                    return result;
            }

            return null;
        }
    }
}

