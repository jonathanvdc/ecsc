using System;
using System.Collections.Generic;
using System.Linq;

namespace Flame.Ecs
{
    /// <summary>
    /// A static class that defines type inference helpers.
    /// </summary>
    public static class TypeInference
    {
        public static IType GetBestType(IType Left, IType Right, FunctionScope Scope)
        {
            if (Left == null || Right == null)
                return null;
            else if (Scope.HasImplicitConversion(Left, Right))
                return Right;
            else if (Scope.HasImplicitConversion(Right, Left))
                return Left;
            else
                return null;
        }

        public static IType GetBestType(IEnumerable<IType> Types, FunctionScope Scope)
        {
            if (!Types.Any())
                return null;
            else
                return Types.Aggregate((l, r) => GetBestType(l, r, Scope));
        }
    }
}

