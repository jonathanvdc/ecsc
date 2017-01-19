using System;
using System.Collections.Generic;
using Flame.Compiler;
using Flame.Compiler.Expressions;

namespace Flame.Ecs.Semantics
{
    /// <summary>
    /// Conversion rules for the EC# programming language.
    /// </summary>
    public sealed class EcsConversionRules : ConversionRules
    {
        private EcsConversionRules()
        {
        }

        public static readonly EcsConversionRules Instance = new EcsConversionRules();

        /// <summary>
        /// Classifies a conversion of the given expression to the given type.
        /// </summary>
        public override IReadOnlyList<ConversionDescription> ClassifyConversion(
            IExpression Source, IType Type)
        {
            var srcType = Source.Type;

            // TODO: special cases for literals.

            return ClassifyConversion(srcType, Type);
        }

        /// <summary>
        /// Determines if the given type is a primitive number type.
        /// </summary>
        /// <returns><c>true</c> if the given type is a primitive number type; otherwise, <c>false</c>.</returns>
        public static bool IsNumberPrimitive(IType Type)
        {
            return Type.GetIsInteger()
            || Type.GetIsFloatingPoint()
            || Type.Equals(PrimitiveTypes.Char);
        }

        /// <summary>
        /// Checks if the given type is a CLR reference type.
        /// This excludes most primitive types.
        /// </summary>
        public static bool IsClrReferenceType(IType Type)
        {
            return Type.GetIsReferenceType()
                && (!Type.GetIsPrimitive() || PrimitiveTypes.String.Equals(Type));
        }

        private static bool UseExplicitBox(IType SourceType, IType TargetType)
        {
            // TODO: Switch to ConversionExpression.UseExplicitBox when it
            // becomes available in the next version of Flame.
            return !SourceType.GetIsReferenceType()
                && !SourceType.GetIsPointer()
                && TargetType.GetIsReferenceType()
                && !ConversionExpression.Instance.IsNonBoxPointer(TargetType);
        }

        private IReadOnlyList<ConversionDescription> NoConversion(
            IType SourceType, IType TargetType)
        {
            if (SourceType is ErrorType)
            {
                // The error type can be converted to any other type.
                return new ConversionDescription[]
                { 
                    new SimpleConversionDescription(ConversionKind.ReinterpretCast) 
                };
            }
            else
            {
                return new ConversionDescription[0];
            }
        }

        /// <summary>
        /// Classifies a conversion of the given source type to the given target type.
        /// </summary>
        public override IReadOnlyList<ConversionDescription> ClassifyConversion(
            IType SourceType, IType TargetType)
        {
            if (SourceType.Equals(TargetType))
            {
                // Identity conversion.
                return new ConversionDescription[]
                { 
                    new SimpleConversionDescription(ConversionKind.Identity) 
                };
            }
            else if (PrimitiveTypes.Null.Equals(SourceType)
                     && TargetType.GetIsReferenceType())
            {
                // Convert 'null' to any reference type, no
                // questions asked.
                return new ConversionDescription[]
                { 
                    new SimpleConversionDescription(ConversionKind.ReinterpretCast) 
                };
            }

            ConversionKind kind;
            if (primitiveConversions.TryGetValue(
                    Tuple.Create(SourceType, TargetType), out kind))
            {
                // Built-in conversion.
                if (kind == ConversionKind.None)
                {
                    return NoConversion(SourceType, TargetType);
                }
                else
                {
                    return new ConversionDescription[]
                    { 
                        new SimpleConversionDescription(kind)
                    };
                }
            }

            if (SourceType.GetIsEnum() && IsNumberPrimitive(TargetType))
            {
                // enum-to-integer
                return new ConversionDescription[]
                {
                    new SimpleConversionDescription(ConversionKind.EnumToNumberStaticCast)
                };
            }
            else if (TargetType.GetIsEnum() && IsNumberPrimitive(SourceType))
            {
                // integer-to-enum
                return new ConversionDescription[]
                {
                    new SimpleConversionDescription(ConversionKind.NumberToEnumStaticCast)
                };
            }
            else if (ConversionExpression.Instance.UseUnboxValue(SourceType, TargetType))
            {
                return new ConversionDescription[]
                { 
                    new SimpleConversionDescription(ConversionKind.UnboxValueConversion) 
                };
            }
            else if (ConversionExpression.Instance.UseDynamicCast(SourceType, TargetType))
            {
                if (ConversionExpression.Instance.UseReinterpretAsDynamicCast(SourceType, TargetType))
                {
                    // Upcast. 
                    return new ConversionDescription[]
                    { 
                        new SimpleConversionDescription(ConversionKind.ReinterpretCast) 
                    };
                }
                else
                {
                    // Downcast. 
                    return new ConversionDescription[]
                    { 
                        new SimpleConversionDescription(ConversionKind.DynamicCast) 
                    };
                }
            }
            else if (UseExplicitBox(SourceType, TargetType))
            {
                // Boxing conversion.
                return new ConversionDescription[]
                {
                    new SimpleConversionDescription(
                        SourceType.Is(TargetType)
                            ? ConversionKind.ImplicitBoxingConversion
                            : ConversionKind.ExplicitBoxingConversion)
                };
            }

            // TODO: user-defined conversions
            // TODO: pointer conversions

            // Didn't find any applicable conversions.
            return NoConversion(SourceType, TargetType);
        }

        private static readonly Dictionary<Tuple<IType, IType>, ConversionKind> primitiveConversions = 
            new Dictionary<Tuple<IType, IType>, ConversionKind>()
            {
                // string -> T
                { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Boolean), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.String), ConversionKind.Identity },
                { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Char), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Int8), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Int16), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Int32), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Int64), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.UInt8), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.UInt16), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.UInt32), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.UInt64), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Float32), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.String, PrimitiveTypes.Float64), ConversionKind.None },

                // bool -> T
                { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Boolean), ConversionKind.Identity },
                { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.String), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Char), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Int8), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Int16), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Int32), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Int64), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.UInt8), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.UInt16), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.UInt32), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.UInt64), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Float32), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.Boolean, PrimitiveTypes.Float64), ConversionKind.None },

                // char -> T
                { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Boolean), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.String), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Char), ConversionKind.Identity },
                { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Int8), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Int16), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Int32), ConversionKind.ImplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Int64), ConversionKind.ImplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.UInt8), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.UInt16), ConversionKind.ImplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.UInt32), ConversionKind.ImplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.UInt64), ConversionKind.ImplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Float32), ConversionKind.ImplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Char, PrimitiveTypes.Float64), ConversionKind.ImplicitStaticCast },

                // int8 -> T
                { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Boolean), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.String), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Char), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Int8), ConversionKind.Identity },
                { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Int16), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Int32), ConversionKind.ImplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Int64), ConversionKind.ImplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.UInt8), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.UInt16), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.UInt32), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.UInt64), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Float32), ConversionKind.ImplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Int8, PrimitiveTypes.Float64), ConversionKind.ImplicitStaticCast },

                // int16 -> T
                { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Boolean), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.String), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Char), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Int8), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Int16), ConversionKind.Identity },
                { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Int32), ConversionKind.ImplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Int64), ConversionKind.ImplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.UInt8), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.UInt16), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.UInt32), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.UInt64), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Float32), ConversionKind.ImplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Int16, PrimitiveTypes.Float64), ConversionKind.ImplicitStaticCast },

                // int32 -> T
                { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Boolean), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.String), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Char), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Int8), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Int16), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Int32), ConversionKind.Identity },
                { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Int64), ConversionKind.ImplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.UInt8), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.UInt16), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.UInt32), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.UInt64), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Float32), ConversionKind.ImplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Int32, PrimitiveTypes.Float64), ConversionKind.ImplicitStaticCast },

                // int64 -> T
                { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Boolean), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.String), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Char), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Int8), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Int16), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Int32), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Int64), ConversionKind.Identity },
                { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.UInt8), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.UInt16), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.UInt32), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.UInt64), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Float32), ConversionKind.ImplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Int64, PrimitiveTypes.Float64), ConversionKind.ImplicitStaticCast },

                // uint8 -> T
                { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Boolean), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.String), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Char), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Int8), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Int16), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Int32), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Int64), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.UInt8), ConversionKind.Identity },
                { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.UInt16), ConversionKind.ImplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.UInt32), ConversionKind.ImplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.UInt64), ConversionKind.ImplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Float32), ConversionKind.ImplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.UInt8, PrimitiveTypes.Float64), ConversionKind.ImplicitStaticCast },

                // uint16 -> T
                { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Boolean), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.String), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Char), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Int8), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Int16), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Int32), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Int64), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.UInt8), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.UInt16), ConversionKind.Identity },
                { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.UInt32), ConversionKind.ImplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.UInt64), ConversionKind.ImplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Float32), ConversionKind.ImplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.UInt16, PrimitiveTypes.Float64), ConversionKind.ImplicitStaticCast },

                // uint32 -> T
                { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Boolean), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.String), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Char), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Int8), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Int16), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Int32), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Int64), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.UInt8), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.UInt16), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.UInt32), ConversionKind.Identity },
                { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.UInt64), ConversionKind.ImplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Float32), ConversionKind.ImplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.UInt32, PrimitiveTypes.Float64), ConversionKind.ImplicitStaticCast },

                // uint64 -> T
                { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Boolean), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.String), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Char), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Int8), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Int16), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Int32), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Int64), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.UInt8), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.UInt16), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.UInt32), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.UInt64), ConversionKind.Identity },
                { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Float32), ConversionKind.ImplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.UInt64, PrimitiveTypes.Float64), ConversionKind.ImplicitStaticCast },

                // float32 -> T
                { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Boolean), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.String), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Char), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Int8), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Int16), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Int32), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Int64), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.UInt8), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.UInt16), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.UInt32), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.UInt64), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Float32), ConversionKind.Identity },
                { Tuple.Create(PrimitiveTypes.Float32, PrimitiveTypes.Float64), ConversionKind.ImplicitStaticCast },

                // float64 -> T
                { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Boolean), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.String), ConversionKind.None },
                { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Char), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Int8), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Int16), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Int32), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Int64), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.UInt8), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.UInt16), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.UInt32), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.UInt64), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Float32), ConversionKind.ExplicitStaticCast },
                { Tuple.Create(PrimitiveTypes.Float64, PrimitiveTypes.Float64), ConversionKind.Identity },
            };
    }
}

