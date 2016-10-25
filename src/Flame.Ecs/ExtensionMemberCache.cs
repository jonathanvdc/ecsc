using System;
using System.Collections.Generic;
using System.Linq;

namespace Flame.Ecs
{
    /// <summary>
    /// A data structure that memoizes extension member lookups.
    /// </summary>
    public sealed class ExtensionMemberCache : MemberCacheBase
    {
        public ExtensionMemberCache(
            QualifiedBinder binder, GlobalMemberCache globalCache)
        {
            this.binder = binder;
            this.globalCache = globalCache;
            this.directExtMembers = null;
        }

        private QualifiedBinder binder;
        private GlobalMemberCache globalCache;
        private Dictionary<IType, List<ITypeMember>> directExtMembers;

        private void BuildDirectExtensionMemberMap()
        {
            directExtMembers = new Dictionary<IType, List<ITypeMember>>();
            foreach (var extTy in binder.ExtensionTypes)
            {
                foreach (var method in globalCache.GetMembers(extTy).OfType<IMethod>())
                {
                    var firstParam = method.Parameters.FirstOrDefault();
                    if (firstParam == null)
                        continue;

                    var firstParamTy = firstParam.ParameterType.GetRecursiveGenericDeclaration();
                    List<ITypeMember> memberList;
                    if (!directExtMembers.TryGetValue(firstParamTy, out memberList))
                    {
                        memberList = new List<ITypeMember>();
                        directExtMembers[firstParamTy] = memberList;
                    }
                    memberList.Add(method);
                }
            }
        }

        /// <inheritdoc/>
        public override IReadOnlyList<ITypeMember> GetMembers(IType Type)
        {
            if (directExtMembers == null)
                BuildDirectExtensionMemberMap();

            List<ITypeMember> results;
            if (directExtMembers.TryGetValue(Type, out results))
            {
                return results;
            }
            else
            {
                var arr = new List<ITypeMember>();
                directExtMembers[Type] = arr;
                return arr;
            }
        }

        public override void Dispose()
        {
            base.Dispose();
            directExtMembers = null;
            binder = null;
        }
    }
}

