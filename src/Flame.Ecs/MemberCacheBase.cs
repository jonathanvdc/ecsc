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
            this.directMemberCache = new Dictionary<IType, Dictionary<string, List<ITypeMember>>>();
            this.namedMemberCache = new Dictionary<Tuple<IType, string>, ITypeMember[]>();
            this.memberNames = new Dictionary<IType, string[]>();
        }

        // This member cache consists of three dictionaries:
        //
        //     1. `directMemberCache` which maps types to a dictionary of member names to
        //        lists of members defined directly by the types.
        //
        //     2. `namedMemberCache` which maps (type, member name) pairs to arrays of
        //        non-hidden members defined either directly or indirectly by `type`.
        //
        //     3. `memberNames` maps types to the sets of names of type members they define.
        //
        // When type member lookup is performed, all members are first stored by name in
        // the `directMemberCache`. Lookups for specific names then extract lists from
        // `directMemberCache`. The results of those lookups are stored in `namedMemberCache`,
        // so no element is ever extracted more than once from `directMemberCache`.
        private Dictionary<IType, Dictionary<string, List<ITypeMember>>> directMemberCache;
        private Dictionary<Tuple<IType, string>, ITypeMember[]> namedMemberCache;
        private Dictionary<IType, string[]> memberNames;

        /// <summary>
        /// Gets all members that are directly defined by the given type.
        /// </summary>
        public abstract IReadOnlyList<ITypeMember> GetMembers(IType Type);

        /// <summary>
        /// Gets all members that are defined by the given type
        /// or one of its base types and have the given name.
        /// Standard hiding rules apply: once a match is found, 
        /// this method returns.
        /// </summary>
        public IReadOnlyList<ITypeMember> GetAllMembers(IType Type, string Name)
        {
            return GetAllMembers(Tuple.Create(Type, Name));
        }

        /// <summary>
        /// Gets all members that are defined by the given type or
        /// one of its base types. Standard hiding rules apply.
        /// </summary>
        /// <param name="Type">The type for which member lookup is performed.</param>
        /// <returns>
        /// The list of all non-hidden members defined by the given type or
        /// one of its base types.</returns>
        /// <remarks>The use case for this method is to add a "Did you mean ...?"
        /// message to error diagnostics. Don't put calls to this method on
        /// the fast-path.</remarks>
        public IReadOnlyList<ITypeMember> GetAllMembers(IType Type)
        {
            var results = new List<ITypeMember>();
            foreach (var name in GetPotentialMemberNames(Type))
            {
                results.AddRange(GetAllMembers(Type, name));
            }
            return results;
        }

        /// <summary>
        /// Gets the set of all member names defined by the given type
        /// and its base types.
        /// </summary>
        /// <param name="Type">The type to examine.</param>
        /// <returns>A set of member names.</returns>
        private HashSet<string> GetPotentialMemberNames(IType Type)
        {
            // Materialize the member cache.
            GetDirectMemberCache(Type);

            // Initialize the set with all member names defined
            // directly by the type.
            var results = new HashSet<string>(memberNames[Type]);

            // Update the set with all type names from all base
            // types.
            foreach (var baseType in Type.BaseTypes)
            {
                results.UnionWith(GetPotentialMemberNames(baseType));
            }
            return results;
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

            // First, look for members in `ty`.
            var results = new HashSet<ITypeMember>(ExtractDirectMembers(ty, name));

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

        /// <summary>
        /// Extracts the list of type members with the given name from the
        /// given type member cache. If no such list exists, then the empty
        /// sequence is returned.
        /// </summary>
        /// <param name="Cache">The cache of type members for a given type.</param>
        /// <param name="Name">The name of the type members to fetch.</param>
        /// <returns>The list of type members with the given name.</returns>
        private IEnumerable<ITypeMember> ExtractDirectMembersFromCache(
            Dictionary<string, List<ITypeMember>> Cache, string Name)
        {
            List<ITypeMember> result;
            if (Cache.TryGetValue(Name, out result))
            {
                // Remove the list from the cache so that the garbage
                // collector may reclaim it sooner rather than later.
                Cache.Remove(Name);
                return result;
            }
            else
            {
                return Enumerable.Empty<ITypeMember>();
            }
        }

        /// <summary>
        /// Computes the list of type members with the given name defined
        /// directly by the given type on the first invocation. Successive
        /// invocations with the same arguments return empty lists.
        /// </summary>
        /// <param name="Type">The type whose direct members are searched for.</param>
        /// <param name="Name">The name of the members to look for.</param>
        /// <returns>A list of type members.</returns>
        private IEnumerable<ITypeMember> ExtractDirectMembers(IType Type, string Name)
        {
            return ExtractDirectMembersFromCache(GetDirectMemberCache(Type), Name);
        }

        /// <summary>
        /// Gets or creates the direct member cache for the given type.
        /// </summary>
        /// <param name="Type">The type to get or create the direct member cache for.</param>
        /// <returns>The direct member cache for the given type.</returns>
        private Dictionary<string, List<ITypeMember>> GetDirectMemberCache(IType Type)
        {
            Dictionary<string, List<ITypeMember>> typeMemberDictionary;
            if (!directMemberCache.TryGetValue(Type, out typeMemberDictionary))
            {
                typeMemberDictionary = new Dictionary<string, List<ITypeMember>>();
                foreach (var member in GetMembers(Type))
                {
                    var sName = member.Name as SimpleName;
                    if (sName != null)
                    {
                        List<ITypeMember> memberList;
                        if (!typeMemberDictionary.TryGetValue(sName.Name, out memberList))
                        {
                            memberList = new List<ITypeMember>();
                            typeMemberDictionary[sName.Name] = memberList;
                        }
                        memberList.Add(member);
                    }
                }
                directMemberCache[Type] = typeMemberDictionary;
                memberNames[Type] = typeMemberDictionary.Keys.ToArray();
            }
            return typeMemberDictionary;
        }

        public virtual void Dispose()
        {
            directMemberCache = null;
            namedMemberCache = null;
        }
    }
}

