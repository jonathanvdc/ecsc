using Flame;
using Flame.Build;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Pixie;

namespace Flame.Ecs
{
    public sealed class EcsTypeRenderer : TypeRenderer
    {
        private EcsTypeRenderer()
        {

        }

        static EcsTypeRenderer()
        {
            Instance = new EcsTypeRenderer();
        }

        public static EcsTypeRenderer Instance { get; private set; }

        private static Dictionary<IType, string> primitiveDict = new Dictionary<IType, string>()
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

        protected override MarkupNode ConvertPrimitiveType(IType Type)
        {
            if (primitiveDict.ContainsKey(Type))
            {
                return CreateTextNode(primitiveDict[Type]);
            }
            else
            {
                return base.ConvertPrimitiveType(Type);
            }
        }

        public override MarkupNode MakePointerType(
            MarkupNode ElementType, PointerKind Kind, IAttributes PointerStyle)
        {
            if (Kind.Equals(PointerKind.ReferencePointer))
            {
                return new MarkupNode(
                    "rendered_pointer_type",
                    new MarkupNode[]
                    {
                        CreateTextNode("ref ", PointerStyle),
                        ElementType
                    });
            }
            else
            {
                return base.MakePointerType(ElementType, Kind, PointerStyle);
            }
        }

        /// <summary>
        /// Gets the set of C# primitive types that have a keyword alias.
        /// </summary>
        /// <returns>C# primitive types that have a keyword alias.</returns>
        public static IEnumerable<IType> KeywordPrimitiveTypes
        {
            get { return primitiveDict.Keys; }
        }
    }
}
