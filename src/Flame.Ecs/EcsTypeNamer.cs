using Flame;
using Flame.Build;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Flame.Ecs
{
    public sealed class EcsTypeNamer : TypeNamerBase
    {
        private EcsTypeNamer()
        {

        }

        static EcsTypeNamer()
        {
            Instance = new EcsTypeNamer();
        }

        public static EcsTypeNamer Instance { get; private set; }

        private Dictionary<IType, string> primitiveDict = new Dictionary<IType, string>()
        {
            { PrimitiveTypes.Int8, "sbyte" },
            { PrimitiveTypes.Int16, "short" },
            { PrimitiveTypes.Int32, "int" },
            { PrimitiveTypes.Int64, "long" },
            { PrimitiveTypes.UInt8, "byte" },
            { PrimitiveTypes.UInt16, "ushort" },
            { PrimitiveTypes.UInt32, "uint" },
            { PrimitiveTypes.UInt64, "ulong" },
            { PrimitiveTypes.Float32, "float" },
            { PrimitiveTypes.Float64, "double" },
            { PrimitiveTypes.Boolean, "bool" },
            { PrimitiveTypes.Char, "char" },
            { PrimitiveTypes.String, "string" },
            { PrimitiveTypes.Void, "void" },
            { PrimitiveTypes.Null, "null" }
        };

        protected override string ConvertPrimitiveType(IType Type)
        {
            if (primitiveDict.ContainsKey(Type))
            {
                return primitiveDict[Type];
            }
            else
            {
                return base.ConvertPrimitiveType(Type);
            }
        }

        protected override string ConvertPointerType(PointerType Type)
        {
            if (Type.PointerKind.Equals(PointerKind.ReferencePointer))
            {
                return "ref " + Convert(Type.ElementType);
            }
            else
            {
                return base.ConvertPointerType(Type);
            }
        }
    }
}
