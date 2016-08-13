using System;
using System.Collections.Generic;

namespace Flame.Ecs.Semantics
{
    public static class BinaryOperatorResolution
    {
        // Describes operand types for enum ops
        private enum EnumOperand
        {
            EnumType,
            UnderlyingType,
            Boolean
        }

        /// <summary>
        /// Tries to get the type that the given operator's operands should be
        /// cast to, for the given combination of initial operand types.
        /// A boolean is returned that tells whether the lookup was successful.
        /// If so, then a 'null' result type indicates that the given operation
        /// is illegal. 
        /// </summary>
        public static bool TryGetPrimitiveOperatorType(
            Operator Op, IType Left, IType Right, out IType Result)
        {
            Result = null;
            Dictionary<Tuple<IType, IType>, IType> dict;
            return opTypes.TryGetValue(Op, out dict)
            && dict.TryGetValue(Tuple.Create(Left, Right), out Result);
        }

        public static IType GetUnderlyingType(IType EnumType)
        {
            return EnumType.GetParent();
        }

        public static bool TryGetEnumOperatorType(
            Operator Op, IType Left, IType Right, 
            out IType UnderlyingType, out IType Result)
        {
            bool lEnum = Left.GetIsEnum();
            bool rEnum = Right.GetIsEnum();

            EnumOperand lOp;
            EnumOperand rOp;

            IType enumTy;
            if (lEnum && rEnum)
            {
                if (!Left.Equals(Right))
                {
                    Result = null;
                    UnderlyingType = null;
                    return false;
                }

                // Internal enum operations
                enumTy = Left;
                UnderlyingType = GetUnderlyingType(Left);
                lOp = EnumOperand.EnumType;
                rOp = EnumOperand.EnumType;
            }
            else if (lEnum)
            {
                enumTy = Left;
                UnderlyingType = GetUnderlyingType(Left);
                lOp = EnumOperand.EnumType;
                rOp = EnumOperand.UnderlyingType;
            }
            else if (rEnum)
            {
                enumTy = Right;
                UnderlyingType = GetUnderlyingType(Right);
                lOp = EnumOperand.UnderlyingType;
                rOp = EnumOperand.EnumType;
            }
            else
            {
                Result = null;
                UnderlyingType = null;
                return false;
            }

            EnumOperand resultOp;
            if (enumOpTypes.TryGetValue(Tuple.Create(Op, lOp, rOp), out resultOp))
            {
                switch (resultOp)
                {
                    case EnumOperand.Boolean:
                        Result = PrimitiveTypes.Boolean;
                        break;
                    case EnumOperand.EnumType:
                        Result = enumTy;
                        break;
                    case EnumOperand.UnderlyingType:
                    default:
                        Result = UnderlyingType;
                        break;
                }
                return true;
            }
            else
            {
                Result = null;
                UnderlyingType = null;
                return false;
            }
        }

        // Overload resolution for X * / - % < > <= >= Y
        // null indicates an error
        private static readonly Dictionary<Tuple<IType, IType>, IType> arithmeticTypes = new Dictionary<Tuple<IType, IType>, IType>()
        {
            // String
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Char), null },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Int8), null },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Int16), null },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Int32), null },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Int64), null },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.UInt8), null },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.UInt16), null },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.UInt32), null },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.UInt64), null },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Float32), null },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Float64), null },

            // Boolean
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Char), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Int8), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Int16), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Int32), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Int64), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.UInt8), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.UInt16), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.UInt32), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.UInt64), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Float32), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Float64), null },

            // Char
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Char), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Int8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Int16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Int32), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Int64), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.UInt8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.UInt16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.UInt32), PrimitiveTypes.UInt32 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.UInt64), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Float32), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Float64), PrimitiveTypes.Float64 },

            // Int8
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Char), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Int8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Int16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Int32), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Int64), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.UInt8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.UInt16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.UInt32), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.UInt64), null },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Float32), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Float64), PrimitiveTypes.Float64 },

            // Int16
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Char), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Int8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Int16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Int32), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Int64), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.UInt8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.UInt16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.UInt32), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.UInt64), null },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Float32), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Float64), PrimitiveTypes.Float64 },

            // Int32
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Char), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Int8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Int16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Int32), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Int64), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.UInt8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.UInt16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.UInt32), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.UInt64), null },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Float32), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Float64), PrimitiveTypes.Float64 },

            // Int64			
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Char), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Int8), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Int16), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Int32), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Int64), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.UInt8), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.UInt16), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.UInt32), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.UInt64), null },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Float32), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Float64), PrimitiveTypes.Float64 },

            // UInt8
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Char), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Int8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Int16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Int32), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Int64), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.UInt8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.UInt16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.UInt32), PrimitiveTypes.UInt32 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.UInt64), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Float32), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Float64), PrimitiveTypes.Float64 },

            // UInt16
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Char), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Int8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Int16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Int32), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Int64), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.UInt8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.UInt16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.UInt32), PrimitiveTypes.UInt32 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.UInt64), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Float32), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Float64), PrimitiveTypes.Float64 },

            // UInt32
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Char), PrimitiveTypes.UInt32 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Int8), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Int16), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Int32), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Int64), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.UInt8), PrimitiveTypes.UInt32 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.UInt16), PrimitiveTypes.UInt32 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.UInt32), PrimitiveTypes.UInt32 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.UInt64), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Float32), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Float64), PrimitiveTypes.Float64 },

            // UInt64
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Char), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Int8), null },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Int16), null },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Int32), null },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Int64), null },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.UInt8), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.UInt16), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.UInt32), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.UInt64), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Float32), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Float64), PrimitiveTypes.Float64 },

            // Float32
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Char), null },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Int8), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Int16), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Int32), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Int64), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.UInt8), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.UInt16), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.UInt32), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.UInt64), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Float32), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Float64), PrimitiveTypes.Float64 },

            // Float64
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Char), null },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Int8), PrimitiveTypes.Float64 },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Int16), PrimitiveTypes.Float64 },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Int32), PrimitiveTypes.Float64 },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Int64), PrimitiveTypes.Float64 },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.UInt8), PrimitiveTypes.Float64 },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.UInt16), PrimitiveTypes.Float64 },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.UInt32), PrimitiveTypes.Float64 },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.UInt64), PrimitiveTypes.Float64 },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Float32), PrimitiveTypes.Float64 },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Float64), PrimitiveTypes.Float64 },
        };

        // Overload resolution for X + Y
        // null indicates an error
        private static readonly Dictionary<Tuple<IType, IType>, IType> additionTypes = new Dictionary<Tuple<IType, IType>, IType>()
        {
            // String
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.String), PrimitiveTypes.String },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Boolean), PrimitiveTypes.String },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Char), PrimitiveTypes.String },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Int8), PrimitiveTypes.String },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Int16), PrimitiveTypes.String },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Int32), PrimitiveTypes.String },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Int64), PrimitiveTypes.String },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.UInt8), PrimitiveTypes.String },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.UInt16), PrimitiveTypes.String },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.UInt32), PrimitiveTypes.String },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.UInt64), PrimitiveTypes.String },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Float32), PrimitiveTypes.String },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Float64), PrimitiveTypes.String },

            // Boolean
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.String), PrimitiveTypes.String },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Char), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Int8), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Int16), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Int32), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Int64), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.UInt8), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.UInt16), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.UInt32), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.UInt64), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Float32), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Float64), null },

            // Char
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.String), PrimitiveTypes.String },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Char), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Int8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Int16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Int32), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Int64), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.UInt8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.UInt16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.UInt32), PrimitiveTypes.UInt32 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.UInt64), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Float32), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Float64), PrimitiveTypes.Float64 },

            // Int8
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.String), PrimitiveTypes.String },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Char), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Int8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Int16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Int32), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Int64), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.UInt8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.UInt16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.UInt32), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.UInt64), null },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Float32), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Float64), PrimitiveTypes.Float64 },

            // Int16
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.String), PrimitiveTypes.String },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Char), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Int8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Int16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Int32), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Int64), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.UInt8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.UInt16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.UInt32), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.UInt64), null },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Float32), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Float64), PrimitiveTypes.Float64 },

            // Int32
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.String), PrimitiveTypes.String },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Char), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Int8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Int16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Int32), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Int64), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.UInt8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.UInt16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.UInt32), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.UInt64), null },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Float32), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Float64), PrimitiveTypes.Float64 },

            // Int64			
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.String), PrimitiveTypes.String },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Char), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Int8), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Int16), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Int32), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Int64), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.UInt8), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.UInt16), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.UInt32), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.UInt64), null },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Float32), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Float64), PrimitiveTypes.Float64 },

            // UInt8
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.String), PrimitiveTypes.String },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Char), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Int8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Int16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Int32), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Int64), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.UInt8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.UInt16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.UInt32), PrimitiveTypes.UInt32 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.UInt64), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Float32), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Float64), PrimitiveTypes.Float64 },

            // UInt16
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Char), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Int8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Int16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Int32), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Int64), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.UInt8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.UInt16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.UInt32), PrimitiveTypes.UInt32 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.UInt64), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Float32), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Float64), PrimitiveTypes.Float64 },

            // UInt32
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.String), PrimitiveTypes.String },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Char), PrimitiveTypes.UInt32 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Int8), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Int16), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Int32), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Int64), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.UInt8), PrimitiveTypes.UInt32 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.UInt16), PrimitiveTypes.UInt32 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.UInt32), PrimitiveTypes.UInt32 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.UInt64), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Float32), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Float64), PrimitiveTypes.Float64 },

            // UInt64
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.String), PrimitiveTypes.String },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Char), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Int8), null },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Int16), null },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Int32), null },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Int64), null },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.UInt8), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.UInt16), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.UInt32), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.UInt64), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Float32), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Float64), PrimitiveTypes.Float64 },

            // Float32
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.String), PrimitiveTypes.String },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Char), null },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Int8), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Int16), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Int32), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Int64), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.UInt8), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.UInt16), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.UInt32), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.UInt64), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Float32), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Float64), PrimitiveTypes.Float64 },

            // Float64
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.String), PrimitiveTypes.String },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Char), null },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Int8), PrimitiveTypes.Float64 },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Int16), PrimitiveTypes.Float64 },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Int32), PrimitiveTypes.Float64 },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Int64), PrimitiveTypes.Float64 },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.UInt8), PrimitiveTypes.Float64 },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.UInt16), PrimitiveTypes.Float64 },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.UInt32), PrimitiveTypes.Float64 },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.UInt64), PrimitiveTypes.Float64 },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Float32), PrimitiveTypes.Float64 },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Float64), PrimitiveTypes.Float64 },
        };

        // Overload resolution for X << >> Y
        // null indicates an error
        private static readonly Dictionary<Tuple<IType, IType>, IType> shiftTypes = new Dictionary<Tuple<IType, IType>, IType>()
        {
            // String
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Char), null },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Int8), null },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Int16), null },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Int32), null },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Int64), null },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.UInt8), null },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.UInt16), null },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.UInt32), null },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.UInt64), null },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Float32), null },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Float64), null },

            // Boolean
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Char), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Int8), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Int16), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Int32), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Int64), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.UInt8), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.UInt16), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.UInt32), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.UInt64), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Float32), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Float64), null },

            // Char
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Char), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Int8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Int16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Int32), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Int64), null },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.UInt8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.UInt16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.UInt32), null },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.UInt64), null },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Float32), null },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Float64), null },

            // Int8
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Char), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Int8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Int16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Int32), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Int64), null },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.UInt8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.UInt16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.UInt32), null },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.UInt64), null },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Float32), null },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Float64), null },

            // Int16
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Char), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Int8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Int16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Int32), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Int64), null },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.UInt8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.UInt16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.UInt32), null },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.UInt64), null },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Float32), null },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Float64), null },

            // Int32
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Char), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Int8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Int16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Int32), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Int64), null },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.UInt8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.UInt16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.UInt32), null },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.UInt64), null },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Float32), null },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Float64), null },

            // Int64
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Char), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Int8), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Int16), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Int32), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Int64), null },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.UInt8), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.UInt16), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.UInt32), null },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.UInt64), null },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Float32), null },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Float64), null },

            // UInt8
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Char), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Int8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Int16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Int32), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Int64), null },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.UInt8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.UInt16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.UInt32), null },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.UInt64), null },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Float32), null },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Float64), null },

            // UInt16
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Char), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Int8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Int16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Int32), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Int64), null },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.UInt8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.UInt16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.UInt32), null },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.UInt64), null },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Float32), null },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Float64), null },

            // UInt32
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Char), PrimitiveTypes.UInt32 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Int8), PrimitiveTypes.UInt32 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Int16), PrimitiveTypes.UInt32 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Int32), PrimitiveTypes.UInt32 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Int64), null },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.UInt8), PrimitiveTypes.UInt32 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.UInt16), PrimitiveTypes.UInt32 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.UInt32), null },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.UInt64), null },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Float32), null },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Float64), null },

            // UInt64
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Char), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Int8), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Int16), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Int32), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Int64), null },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.UInt8), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.UInt16), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.UInt32), null },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.UInt64), null },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Float32), null },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Float64), null },

            // Float32
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Char), null },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Int8), null },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Int16), null },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Int32), null },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Int64), null },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.UInt8), null },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.UInt16), null },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.UInt32), null },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.UInt64), null },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Float32), null },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Float64), null },

            // Float64
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Char), null },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Int8), null },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Int16), null },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Int32), null },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Int64), null },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.UInt8), null },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.UInt16), null },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.UInt32), null },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.UInt64), null },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Float32), null },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Float64), null },
        };

        // Overload resolution for X | & ^ || && Y
        private static readonly Dictionary<Tuple<IType, IType>, IType> logicalTypes = new Dictionary<Tuple<IType, IType>, IType>()
        {
            // String
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Char), null },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Int8), null },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Int16), null },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Int32), null },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Int64), null },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.UInt8), null },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.UInt16), null },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.UInt32), null },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.UInt64), null },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Float32), null },
            { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Float64), null },

            // Boolean
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Boolean), PrimitiveTypes.Boolean },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Char), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Int8), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Int16), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Int32), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Int64), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.UInt8), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.UInt16), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.UInt32), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.UInt64), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Float32), null },
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Float64), null },

            // Char
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Char), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Int8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Int16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Int32), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Int64), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.UInt8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.UInt16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.UInt32), PrimitiveTypes.UInt32 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.UInt64), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Float32), null },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Float64), null },

            // Int8
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Char), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Int8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Int16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Int32), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Int64), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.UInt8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.UInt16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.UInt32), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.UInt64), null },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Float32), null },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Float64), null },

            // Int16
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Char), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Int8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Int16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Int32), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Int64), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.UInt8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.UInt16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.UInt32), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.UInt64), null },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Float32), null },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Float64), null },

            // Int32
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Char), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Int8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Int16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Int32), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Int64), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.UInt8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.UInt16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.UInt32), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.UInt64), null },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Float32), null },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Float64), null },

            // Int64
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Char), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Int8), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Int16), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Int32), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Int64), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.UInt8), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.UInt16), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.UInt32), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.UInt64), null },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Float32), null },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Float64), null },

            // UInt8
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Char), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Int8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Int16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Int32), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Int64), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.UInt8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.UInt16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.UInt32), PrimitiveTypes.UInt32 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.UInt64), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Float32), null },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Float64), null },

            // UInt16
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Char), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Int8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Int16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Int32), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Int64), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.UInt8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.UInt16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.UInt32), PrimitiveTypes.UInt32 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.UInt64), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Float32), null },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Float64), null },

            // UInt32
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Char), PrimitiveTypes.UInt32 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Int8), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Int16), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Int32), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Int64), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.UInt8), PrimitiveTypes.UInt32 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.UInt16), PrimitiveTypes.UInt32 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.UInt32), PrimitiveTypes.UInt32 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.UInt64), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Float32), null },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Float64), null },

            // UInt64
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Char), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Int8), null },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Int16), null },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Int32), null },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Int64), null },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.UInt8), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.UInt16), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.UInt32), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.UInt64), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Float32), null },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Float64), null },

            // Float32
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Char), null },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Int8), null },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Int16), null },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Int32), null },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Int64), null },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.UInt8), null },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.UInt16), null },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.UInt32), null },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.UInt64), null },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Float32), null },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Float64), null },

            // Float64
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.String), null },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Boolean), null },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Char), null },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Int8), null },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Int16), null },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Int32), null },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Int64), null },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.UInt8), null },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.UInt16), null },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.UInt32), null },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.UInt64), null },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Float32), null },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Float64), null },
        };

        // Overload resolution for Y == != X
        // Combinations of types that are not encoded in this table, should
        // be handled by operator resolution.
        private static readonly Dictionary<Tuple<IType, IType>, IType> equalityTypes = new Dictionary<Tuple<IType, IType>, IType>()
        {
            // Boolean
            { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Boolean), PrimitiveTypes.Boolean },

            // Char
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Char), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Int8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Int16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Int32), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Int64), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.UInt8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.UInt16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.UInt32), PrimitiveTypes.UInt32 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.UInt64), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Float32), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Float64), PrimitiveTypes.Float64 },

            // Int8
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Char), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Int8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Int16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Int32), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Int64), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.UInt8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.UInt16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.UInt32), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.UInt64), null },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Float32), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Float64), PrimitiveTypes.Float64 },

            // Int16
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Char), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Int8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Int16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Int32), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Int64), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.UInt8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.UInt16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.UInt32), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.UInt64), null },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Float32), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Float64), PrimitiveTypes.Float64 },

            // Int32
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Char), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Int8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Int16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Int32), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Int64), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.UInt8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.UInt16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.UInt32), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.UInt64), null },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Float32), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Float64), PrimitiveTypes.Float64 },

            // Int64
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Char), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Int8), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Int16), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Int32), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Int64), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.UInt8), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.UInt16), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.UInt32), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.UInt64), null },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Float32), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Float64), PrimitiveTypes.Float64 },

            // UInt8
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Char), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Int8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Int16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Int32), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Int64), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.UInt8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.UInt16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.UInt32), PrimitiveTypes.UInt32 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.UInt64), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Float32), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Float64), PrimitiveTypes.Float64 },

            // UInt16
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Char), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Int8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Int16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Int32), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Int64), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.UInt8), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.UInt16), PrimitiveTypes.Int32 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.UInt32), PrimitiveTypes.UInt32 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.UInt64), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Float32), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Float64), PrimitiveTypes.Float64 },

            // UInt32
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Char), PrimitiveTypes.UInt32 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Int8), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Int16), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Int32), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Int64), PrimitiveTypes.Int64 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.UInt8), PrimitiveTypes.UInt32 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.UInt16), PrimitiveTypes.UInt32 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.UInt32), PrimitiveTypes.UInt32 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.UInt64), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Float32), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Float64), PrimitiveTypes.Float64 },

            // UInt64
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Char), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Int8), null },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Int16), null },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Int32), null },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Int64), null },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.UInt8), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.UInt16), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.UInt32), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.UInt64), PrimitiveTypes.UInt64 },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Float32), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Float64), PrimitiveTypes.Float64 },

            // Float32
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Char), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Int8), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Int16), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Int32), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Int64), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.UInt8), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.UInt16), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.UInt32), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.UInt64), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Float32), PrimitiveTypes.Float32 },
            { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Float64), PrimitiveTypes.Float64 },

            // Float64
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Char), PrimitiveTypes.Float64 },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Int8), PrimitiveTypes.Float64 },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Int16), PrimitiveTypes.Float64 },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Int32), PrimitiveTypes.Float64 },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Int64), PrimitiveTypes.Float64 },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.UInt8), PrimitiveTypes.Float64 },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.UInt16), PrimitiveTypes.Float64 },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.UInt32), PrimitiveTypes.Float64 },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.UInt64), PrimitiveTypes.Float64 },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Float32), PrimitiveTypes.Float64 },
            { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Float64), PrimitiveTypes.Float64 },
        };

        private static readonly Dictionary<Operator, Dictionary<Tuple<IType, IType>, IType>> opTypes = new Dictionary<Operator, Dictionary<Tuple<IType, IType>, IType>>()
        {
            { Operator.Add, additionTypes },
            { Operator.Subtract, arithmeticTypes },
            { Operator.Multiply, arithmeticTypes },
            { Operator.Divide, arithmeticTypes },
            { Operator.Remainder, arithmeticTypes },
            { Operator.LeftShift, shiftTypes },
            { Operator.RightShift, shiftTypes },
            { Operator.CheckEquality, equalityTypes },
            { Operator.CheckInequality, equalityTypes },
            { Operator.CheckLessThan, arithmeticTypes },
            { Operator.CheckGreaterThan, arithmeticTypes },
            { Operator.CheckLessThanOrEqual, arithmeticTypes },
            { Operator.CheckGreaterThanOrEqual, arithmeticTypes },
            { Operator.And, logicalTypes },
            { Operator.Or, logicalTypes },
            { Operator.Xor, logicalTypes }
        };

        private static readonly Dictionary<Tuple<Operator, EnumOperand, EnumOperand>, EnumOperand> enumOpTypes = 
            new Dictionary<Tuple<Operator, EnumOperand, EnumOperand>, EnumOperand>()
            {
                { Tuple.Create(Operator.Add, EnumOperand.EnumType, EnumOperand.UnderlyingType), EnumOperand.EnumType },
                { Tuple.Create(Operator.Add, EnumOperand.UnderlyingType, EnumOperand.EnumType), EnumOperand.EnumType },
                { Tuple.Create(Operator.Subtract, EnumOperand.EnumType, EnumOperand.UnderlyingType), EnumOperand.EnumType },
                { Tuple.Create(Operator.Subtract, EnumOperand.EnumType, EnumOperand.EnumType), EnumOperand.UnderlyingType },

                { Tuple.Create(Operator.And, EnumOperand.EnumType, EnumOperand.EnumType), EnumOperand.EnumType },
                { Tuple.Create(Operator.Or, EnumOperand.EnumType, EnumOperand.EnumType), EnumOperand.EnumType },
                { Tuple.Create(Operator.Xor, EnumOperand.EnumType, EnumOperand.EnumType), EnumOperand.EnumType },

                { Tuple.Create(Operator.CheckEquality, EnumOperand.EnumType, EnumOperand.EnumType), EnumOperand.Boolean },
                { Tuple.Create(Operator.CheckInequality, EnumOperand.EnumType, EnumOperand.EnumType), EnumOperand.Boolean },
                { Tuple.Create(Operator.CheckLessThan, EnumOperand.EnumType, EnumOperand.EnumType), EnumOperand.Boolean },
                { Tuple.Create(Operator.CheckGreaterThan, EnumOperand.EnumType, EnumOperand.EnumType), EnumOperand.Boolean },
                { Tuple.Create(Operator.CheckLessThanOrEqual, EnumOperand.EnumType, EnumOperand.EnumType), EnumOperand.Boolean },
                { Tuple.Create(Operator.CheckGreaterThanOrEqual, EnumOperand.EnumType, EnumOperand.EnumType), EnumOperand.Boolean },
            };
    }
}

