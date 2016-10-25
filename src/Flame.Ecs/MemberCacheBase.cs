using System;
using System.Collections.Generic;
using System.Linq;
using Flame.Collections;

namespace Flame.Ecs
{
    /// <summary>
    /// A base class for data structures that memoize member lookups.
    /// </summary>
    public abstract class MemberCacheBase : IDisposable
    {
        public MemberCacheBase()
        {
            this.namedMemberCache = new Dictionary<Tuple<IType, string>, ITypeMember[]>();
        }

        private Dictionary<Tuple<IType, string>, ITypeMember[]> namedMemberCache;

        /// <summary>
        /// Gets all members that are directly defined by the given type.
        /// </summary>
        public abstract IReadOnlyList<ITypeMember> GetMembers(IType Type);

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

        /// <summary>
        /// Checks if any of the members in the given set will hide
        /// any and all members from the base types.
        /// </summary>
        protected bool HidesAll(HashSet<ITypeMember> Members)
        {
            foreach (var item in Members)
            {
                if (!(item is IMethod))
                    return true;
            }
            return false;
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

        public virtual void Dispose()
        {
            namedMemberCache = null;
        }
    }
}

