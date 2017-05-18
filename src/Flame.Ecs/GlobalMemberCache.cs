using System;
using System.Collections.Generic;
using System.Linq;
using Flame.Collections;

namespace Flame.Ecs
{
    /// <summary>
    /// A data structure that memoizes member lookups.
    /// </summary>
    public sealed class GlobalMemberCache : MemberCacheBase
    {
        public GlobalMemberCache(IEnvironment Environment)
        {
            this.Environment = Environment;
            this.globalMemberCache = new Dictionary<IType, ITypeMember[]>();
            this.indexerCache = new Dictionary<IType, IProperty[]>();
            this.operatorCache = new Dictionary<IType, SmallMultiDictionary<Operator, IMethod>>();
        }

        /// <summary>
        /// Gets the runtime environment description for this member cache.
        /// </summary>
        /// <returns>A description of this member cache's runtime environment.</returns>
        public IEnvironment Environment { get; private set; }

        private Dictionary<IType, ITypeMember[]> globalMemberCache;
        private Dictionary<IType, IProperty[]> indexerCache;
        private Dictionary<IType, SmallMultiDictionary<Operator, IMethod>> operatorCache;

        /// <summary>
        /// Gets all members that are directly defined by the given type.
        /// </summary>
        public override IReadOnlyList<ITypeMember> GetMembers(IType Type)
        {
            ITypeMember[] result;
            if (globalMemberCache.TryGetValue(Type, out result))
            {
                return result;
            }
            else
            {
                var envType = Environment.GetEquivalentType(Type);
                result = envType.Fields
                    .Concat<ITypeMember>(envType.Properties)
                    .Concat<ITypeMember>(envType.Methods)
                    .ToArray();
                globalMemberCache[Type] = result;
                return result;
            }
        }

        /// <inheritdoc/>
        public override IReadOnlyList<IType> GetBaseTypes(IType Type)
        {
            var results = Environment.GetEquivalentType(Type).BaseTypes.ToArray();
            if (Type.GetIsGenericParameter() && results.All(t => t.GetIsInterface()))
            {
                var extendedResults = new IType[results.Length + 1];
                extendedResults[0] = Environment.RootType;
                Array.Copy(results, 0, extendedResults, 1, results.Length);
                return extendedResults;
            }
            else
            {
                return results;
            }
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

        private SmallMultiDictionary<Operator, IMethod> LookupAllOperators(IType Type)
        {
            var results = new SmallMultiDictionary<Operator, IMethod>();

            foreach (var m in GetMembers(Type).OfType<IMethod>())
            {
                var op = m.GetOperator();
                if (op.IsDefined)
                {
                    results.Add(op, m);
                }
            }

            // Then look in the base type.
            var bType = Type.GetParent();
            if (bType != null)
            {
                results.AddRange(GetAllOperators(bType));
            }

            return results;
        }

        /// <summary>
        /// Gets all visible operators that are defined by
        /// the given type or one of its base types.
        /// </summary>
        public SmallMultiDictionary<Operator, IMethod> GetAllOperators(IType Type)
        {
            SmallMultiDictionary<Operator, IMethod> result;
            if (operatorCache.TryGetValue(Type, out result))
            {
                return result;
            }
            else
            {
                var dict = LookupAllOperators(Type);
                operatorCache[Type] = dict;
                return dict;
            }
        }

        /// <summary>
        /// Gets all visible operators that are defined by
        /// the given type or one of its base types.
        /// </summary>
        public IEnumerable<IMethod> GetAllOperators(IType Type, Operator Op)
        {
            return GetAllOperators(Type).GetAll(Op);
        }

        public override void Dispose()
        {
            base.Dispose();
            globalMemberCache = null;
            indexerCache = null;
            operatorCache = null;
        }
    }
}

