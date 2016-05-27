using System;
using System.Collections.Generic;

namespace Flame.Ecs.Semantics
{
    public static class UnaryOperatorResolution
    {
        // Define '~' as the bitwise complement.
        public static readonly Operator BitwiseComplement = 
            Operator.GetOperator("~");

        /// <summary>
        /// Tries to get the type that the given operator's operand should be
        /// cast to, for the given operand type.
        /// A boolean is returned that tells whether the lookup was successful.
        /// If so, then a 'null' result type indicates that the given operation
        /// is illegal. 
        /// </summary>
        public static bool TryGetPrimitiveOperatorType(
            Operator Op, IType OperandType, out IType Result)
        {
            Result = null;
            Dictionary<IType, IType> dict;
            return opTypes.TryGetValue(Op, out dict) 
                && dict.TryGetValue(OperandType, out Result);
        }

        // Primitive types for the unary plus (@+) operator.
        private static readonly Dictionary<IType, IType> unaryPlusTypes = 
            new Dictionary<IType, IType>()
        {
            { PrimitiveTypes.String, null },
            { PrimitiveTypes.Boolean, null },
            { PrimitiveTypes.Char, PrimitiveTypes.Int32 },
            { PrimitiveTypes.Int8, PrimitiveTypes.Int32 },
            { PrimitiveTypes.Int16, PrimitiveTypes.Int32 },
            { PrimitiveTypes.Int32, PrimitiveTypes.Int32 },
            { PrimitiveTypes.Int64, PrimitiveTypes.Int64 },
            { PrimitiveTypes.UInt8, PrimitiveTypes.Int32 },
            { PrimitiveTypes.UInt16, PrimitiveTypes.Int32 },
            { PrimitiveTypes.UInt32, PrimitiveTypes.UInt32 },
            { PrimitiveTypes.UInt64, PrimitiveTypes.UInt64 },
            { PrimitiveTypes.Float32, PrimitiveTypes.Float32 },
            { PrimitiveTypes.Float64, PrimitiveTypes.Float64 }
        };

        // Primitive types for the unary minus (@-) operator.
        private static readonly Dictionary<IType, IType> unaryMinusTypes = 
            new Dictionary<IType, IType>()
        {
            { PrimitiveTypes.String, null },
            { PrimitiveTypes.Boolean, null },
            { PrimitiveTypes.Char, PrimitiveTypes.Int32 },
            { PrimitiveTypes.Int8, PrimitiveTypes.Int32 },
            { PrimitiveTypes.Int16, PrimitiveTypes.Int32 },
            { PrimitiveTypes.Int32, PrimitiveTypes.Int32 },
            { PrimitiveTypes.Int64, PrimitiveTypes.Int64 },
            { PrimitiveTypes.UInt8, PrimitiveTypes.Int32 },
            { PrimitiveTypes.UInt16, PrimitiveTypes.Int32 },
            { PrimitiveTypes.UInt32, PrimitiveTypes.Int64 },
            { PrimitiveTypes.UInt64, null },
            { PrimitiveTypes.Float32, PrimitiveTypes.Float32 },
            { PrimitiveTypes.Float64, PrimitiveTypes.Float64 }
        };

        // Primitive types for the logical negation (@!) operator.
        private static readonly Dictionary<IType, IType> logicalNegationTypes = 
            new Dictionary<IType, IType>()
        {
            { PrimitiveTypes.String, null },
            { PrimitiveTypes.Boolean, PrimitiveTypes.Boolean },
            { PrimitiveTypes.Char, null },
            { PrimitiveTypes.Int8, null },
            { PrimitiveTypes.Int16, null },
            { PrimitiveTypes.Int32, null },
            { PrimitiveTypes.Int64, null },
            { PrimitiveTypes.UInt8, null },
            { PrimitiveTypes.UInt16, null },
            { PrimitiveTypes.UInt32, null },
            { PrimitiveTypes.UInt64, null },
            { PrimitiveTypes.Float32, null },
            { PrimitiveTypes.Float64, null }
        };

        // Primitive types for the bitwise complement (@~) operator.
        private static readonly Dictionary<IType, IType> bitwiseComplementTypes = 
            new Dictionary<IType, IType>()
        {
            { PrimitiveTypes.String, null },
            { PrimitiveTypes.Boolean, null },
            { PrimitiveTypes.Char, PrimitiveTypes.Int32 },
            { PrimitiveTypes.Int8, PrimitiveTypes.Int32 },
            { PrimitiveTypes.Int16, PrimitiveTypes.Int32 },
            { PrimitiveTypes.Int32, PrimitiveTypes.Int32 },
            { PrimitiveTypes.Int64, PrimitiveTypes.Int64 },
            { PrimitiveTypes.UInt8, PrimitiveTypes.Int32 },
            { PrimitiveTypes.UInt16, PrimitiveTypes.Int32 },
            { PrimitiveTypes.UInt32, PrimitiveTypes.UInt32 },
            { PrimitiveTypes.UInt64, PrimitiveTypes.UInt64 },
            { PrimitiveTypes.Float32, null },
            { PrimitiveTypes.Float64, null }
        };

        private static readonly Dictionary<Operator, Dictionary<IType, IType>> opTypes = 
            new Dictionary<Operator, Dictionary<IType, IType>>()
        {
            { Operator.Add, unaryPlusTypes },
            { Operator.Subtract, unaryMinusTypes },
            { Operator.Not, logicalNegationTypes },
            { BitwiseComplement, bitwiseComplementTypes }
        };
    }
}

