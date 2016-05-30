using System;
using System.Collections.Generic;
using System.Linq;

namespace Flame.Ecs
{
    /// <summary>
    /// A data structure that memoizes member lookups.
    /// </summary>
    public class GlobalMemberCache : IDisposable
    {
        public GlobalMemberCache()
        {
            this.globalMemberCache = new Dictionary<IType, ITypeMember[]>();
            this.namedMemberCache = new Dictionary<Tuple<IType, string>, ITypeMember[]>();
            this.indexerCache = new Dictionary<IType, IProperty[]>();
        }

        private Dictionary<IType, ITypeMember[]> globalMemberCache;
        private Dictionary<Tuple<IType, string>, ITypeMember[]> namedMemberCache;
        private Dictionary<IType, IProperty[]> indexerCache;

        /// <summary>
        /// Gets all members that are directly defined by the given type.
        /// </summary>
        public IReadOnlyList<ITypeMember> GetMembers(IType Type)
        {
            ITypeMember[] result;
            if (globalMemberCache.TryGetValue(Type, out result))
            {
                return result;
            }
            else
            {
                result = Type.Fields
                    .Concat<ITypeMember>(Type.Properties)
                    .Concat<ITypeMember>(Type.Methods)
                    .ToArray();
                globalMemberCache[Type] = result;
                return result;
            }
        }

        /// <summary>
        /// Gets all members that are defined by the given type
        /// or one of its base types, and have the given name.
        /// Standard hiding rules apply: once a match is found, 
        /// this method returns.
        /// </summary>
        private ITypeMember[] GetAllMembers(Tuple<IType, string> Key)
        {
            ITypeMember[] result;
            if (namedMemberCache.TryGetValue(Key, out result))
            {
                return result;
            }
            else
            {

                result = LookupAllMembers(Key);
                namedMemberCache[Key] = result;
                return result;
            }
        }

        private IEnumerable<ITypeMember> GetVisibleMembers(
            IType Type, string Name, IMethod[] HiddenSignatures)
        {
            var members = GetAllMembers(Type, Name);
            if (HiddenSignatures.Length > 0)
            {
                // TODO: This is a silly O(n^2) approach. We could
                // do better if we used hashing.
                return members.OfType<IMethod>().Where(item => 
                    !HiddenSignatures.Any(m => 
                        MethodExtensions.HasSameCallSignature(m, item)));
            }
            else
            {
                return members;
            }
        }

        /// <summary>
        /// Gets all members that are defined by the given type
        /// or one of its base types, and have the given name.
        /// Standard hiding rules apply: once a match is found, 
        /// this method returns.
        /// </summary>
        public IReadOnlyList<ITypeMember> GetAllMembers(IType Type, string Name)
        {
            return GetAllMembers(Tuple.Create(Type, Name));
        }

        private bool HidesAll(HashSet<ITypeMember> Members)
        {
            if (Members.Count == 0)
                return false;

            foreach (var item in Members)
            {
                if (!(item is IMethod))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Actually looks up all members that are defined by 
        /// the given type or one of its base types, 
        /// and have the given name.
        /// Standard hiding rules apply: once a match is found, 
        /// this method returns.
        /// </summary>
        private ITypeMember[] LookupAllMembers(
            Tuple<IType, string> Key)
        {
            var ty = Key.Item1;
            var name = Key.Item2;
            var results = new HashSet<ITypeMember>();
            // First, look in the given type.
            results.UnionWith(GetMembers(ty).Where(m =>
            {
                var sName = m.Name as SimpleName;
                return sName != null && sName.Name == name;
            }));

            if (HidesAll(results))
                return results.ToArray();

            // Then try the base type.
            var bType = ty.GetParent();
            if (bType != null)
            {
                results.UnionWith(
                    GetVisibleMembers(
                        bType, name, 
                        results.OfType<IMethod>().ToArray()));

                if (results.Count > 0)
                    return results.ToArray();
            }

            // Okay, so that didn't work. Maybe we'll
            // have more luck in examining the interfaces.
            var hidingMethods = results.OfType<IMethod>().ToArray();
            foreach (var inter in ty.GetInterfaces())
            {
                results.UnionWith(GetVisibleMembers(
                    inter, name, hidingMethods));
            }
            return results.ToArray();
        }

        private IEnumerable<IProperty> GetVisibleIndexers(
            IType Type, HashSet<IProperty> HiddenSignatures)
        {
            var members = GetAllIndexers(Type);
            if (HiddenSignatures.Count > 0)
            {
                // TODO: This is a silly O(n^2) approach. We could
                // do better if we used hashing.
                return members.OfType<IProperty>().Where(item => 
                    !HiddenSignatures.Any(p => 
                        PropertyExtensions.HasSameCallSignature(p, item)));
            }
            else
            {
                return members;
            }
        }

        private IProperty[] LookupAllIndexers(IType Type)
        {
            var results = new HashSet<IProperty>();
            // Perhaps the given type actually 
            // declares members with this name?
            results.UnionWith(
                GetMembers(Type)
                .OfType<IProperty>()
                .Where(p => p.GetIsIndexer()));

            // Then look in the base type.
            var bType = Type.GetParent();
            if (bType != null)
            {
                results.UnionWith(GetVisibleIndexers(
                    bType, results));

                if (results.Count > 0)
                    return results.ToArray();
            }

            // Okay, so that didn't work. Maybe we'll
            // be luckier when examining the interfaces.
            var hidingIndexers = new HashSet<IProperty>(results);
            foreach (var inter in Type.GetInterfaces())
            {
                results.UnionWith(GetVisibleIndexers(
                    inter, hidingIndexers));
            }
            return results.ToArray();
        }

        /// <summary>
        /// Gets all visible indexers that are defined by
        /// the given type or one of its base types.
        /// </summary>
        public IReadOnlyList<IProperty> GetAllIndexers(IType Type)
        {
            IProperty[] result;
            if (indexerCache.TryGetValue(Type, out result))
            {
                return result;
            }
            else
            {
                result = LookupAllIndexers(Type);
                indexerCache[Type] = result;
                return result;
            }
        }

        public void Dispose()
        {
            globalMemberCache = null;
            namedMemberCache = null;
            indexerCache = null;
        }
    }
}

