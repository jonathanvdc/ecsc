using System;
using System.Collections.Generic;
using System.Linq;
using Flame.Compiler;
using Flame.Compiler.Expressions;

namespace Flame.Ecs.Semantics
{
    /// <summary>
    /// Conversion rules for the EC# programming language.
    /// </summary>
    public sealed class EcsConversionRules : ConversionRules
    {
        public EcsConversionRules(IEnvironment Environment)
        {
            this.Environment = Environment;
        }

        /// <summary>
        /// Gets a description of the run-time environment of the target platform.
        /// </summary>
        /// <returns>The run-time environment of the target platform.</returns>
        public IEnvironment Environment { get; private set; }

        /// <summary>
        /// Classifies a conversion of the given expression to the given type.
        /// </summary>
        public override IReadOnlyList<ConversionDescription> ClassifyConversion(
            IExpression Source, IType TargetType, FunctionScope Scope)
        {
            var srcType = Source.Type;

            if (srcType.Equals(TargetType))
            {
                // Identity conversion.
                return new ConversionDescription[] { ConversionDescription.Identity };
            }

            if (Source is IntegerExpression)
            {
                var srcLiteral = ((IntegerExpression)Source).Value;
                var targetSpec = TargetType.GetIntegerSpec();
                if (targetSpec != null && targetSpec.IsRepresentible(srcLiteral.Value))
                {
                    return new ConversionDescription[] { ConversionDescription.ImplicitStaticCast };
                }
            }

            // Handle method group conversions.
            if (TargetType.GetIsDelegate())
            {
                var sourceDelegates = IntersectionExpression.GetIntersectedExpressions(
                    Source.GetEssentialExpression())
                    .Where(item => item.Type is MethodType)
                    .ToArray();
                if (sourceDelegates.Length > 0)
                {
                    return ClassifyMethodGroupConversion(
                        sourceDelegates, MethodType.GetMethod(TargetType), Scope);
                }
            }

            // TODO: more special cases for literals.

            return ClassifyConversion(srcType, TargetType, Scope);
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
        /// Checks if the given type is a C# reference type.
        /// This excludes enum and non-box pointer types.
        /// </summary>
        public static bool IsCSharpReferenceType(IType Type)
        {
            return Type.GetIsReferenceType()
                && !Type.GetIsEnum()
                && !ConversionExpression.Instance.IsNonBoxPointer(Type);
        }

        /// <summary>
        /// Checks if the given type is a C# value type.
        /// This includes enum types.
        /// </summary>
        public static bool IsCSharpValueType(IType Type)
        {
            return Type.GetIsValueType() || Type.GetIsEnum();
        }

        private static ConversionDescription NoConversion(
            IType SourceType, IType TargetType)
        {
            if (SourceType is ErrorType)
            {
                // The error type can be converted to any other type.
                return ConversionDescription.ReinterpretCast;
            }
            else
            {
                return ConversionDescription.None;
            }
        }

        /// <summary>
        /// Classifies a conversion of the given source type to the given target type.
        /// </summary>
        public override IReadOnlyList<ConversionDescription> ClassifyConversion(
            IType SourceType, IType TargetType, FunctionScope Scope)
        {
            // Look for conversions in this order:
            //
            //     1. Implicit built-in conversions
            //     2. Implicit user-defined conversions
            //     3. Explicit built-in conversions
            //     4. Explicit user-defined conversions
            //

            // Try implicit built-in conversions.
            var builtinConv = ClassifyBuiltinConversion(SourceType, TargetType);
            if (builtinConv.Exists && builtinConv.IsImplicit)
                return new ConversionDescription[] { builtinConv };

            // Try implicit user-defined conversions.
            var implicitUserDefConv = ClassifyUserDefinedConversion(
                SourceType, TargetType, Scope, false);

            if (implicitUserDefConv.Count > 0)
                return implicitUserDefConv;

            // Try explicit built-in conversions.
            if (builtinConv.Exists)
                return new ConversionDescription[] { builtinConv };

            // Try explicit user-defined conversions.
            return ClassifyUserDefinedConversion(
                SourceType, TargetType, Scope, true);
        }

        /// <summary>
        /// Classifies a conversion of the given source type to the given target type.
        /// User-defined conversions are not considered.
        /// </summary>
        public override ConversionDescription ClassifyBuiltinConversion(
            IType SourceType, IType TargetType)
        {
            if (SourceType.Equals(TargetType))
            {
                // Identity conversion.
                return ConversionDescription.Identity;
            }
            else if (PrimitiveTypes.Null.Equals(SourceType)
                && IsCSharpReferenceType(TargetType))
            {
                // Convert 'null' to any reference type, no
                // questions asked.
                return ConversionDescription.ReinterpretCast;
            }

            ConversionDescription primitiveConvDesc;
            if (primitiveConversions.TryGetValue(
                new KeyValuePair<IType, IType>(SourceType, TargetType), out primitiveConvDesc))
            {
                // Built-in conversion.
                if (primitiveConvDesc.Kind == ConversionKind.None)
                {
                    return NoConversion(SourceType, TargetType);
                }
                else
                {
                    return primitiveConvDesc;
                }
            }

            if (SourceType.GetIsEnum())
            {
                if (IsNumberPrimitive(TargetType))
                {
                    // enum-to-integer
                    return ConversionDescription.EnumToNumberStaticCast;
                }
                else if (TargetType.GetIsEnum())
                {
                    // enum-to-enum
                    return ConversionDescription.EnumToEnumStaticCast;
                }
            }

            if (TargetType.GetIsEnum() && IsNumberPrimitive(SourceType))
            {
                // integer-to-enum
                return ConversionDescription.NumberToEnumStaticCast;
            }

            var envSourceType = Environment.GetEquivalentType(SourceType);
            var envTargetType = Environment.GetEquivalentType(TargetType);
            if (IsCSharpReferenceType(envSourceType)
                && (IsCSharpValueType(envTargetType) ||
                    (envTargetType.GetIsGenericParameter()
                    && !IsCSharpReferenceType(envTargetType)))
                && envTargetType.Is(envSourceType))
            {
                // Here's what the C# spec says about unboxing conversions:
                //
                //     An unboxing conversion permits a reference type to be explicitly
                //     converted to a *value_type*. An unboxing conversion exists from the
                //     types `object`, `dynamic` and `System.ValueType` to any
                //     *non_nullable_value_type*, and from any *interface_type* to any
                //     *non_nullable_value_type* that implements the *interface_type*.
                //     Furthermore type `System.Enum` can be unboxed to any *enum_type*.
                //
                //     [...]
                //
                //     The following explicit conversions exist for a given type parameter `T`:
                //
                //         *  From the effective base class `C` of `T` to `T` and from any
                //            base class of `C` to `T`. At run-time, if `T` is a value type,
                //            the conversion is executed as an unboxing conversion. Otherwise,
                //            the conversion is executed as an explicit reference conversion or
                //            identity conversion.
                //
                //         *  From any interface type to `T`. At run-time, if `T` is a value type,
                //            the conversion is executed as an unboxing conversion. Otherwise, the
                //            conversion is executed as an explicit reference conversion or identity
                //            conversion.
                //
                //         *  From `T` to any *interface_type* `I` provided there is not already an
                //            implicit conversion from `T` to `I`. At run-time, if `T` is a value type,
                //            the conversion is executed as a boxing conversion followed by an explicit
                //            reference conversion. Otherwise, the conversion is executed as an explicit
                //            reference conversion or identity conversion.
                //
                //         *  From a type parameter `U` to `T`, provided `T` depends on `U`.
                //            At run-time, if `U` is a value type, then `T` and `U` are necessarily
                //            the same type and no conversion is performed. Otherwise, if `T` is a
                //            value type, the conversion is executed as an unboxing conversion.
                //            Otherwise, the conversion is executed as an explicit reference
                //            conversion or identity conversion.
                //
                //     If `T` is known to be a reference type, the conversions above are all classified
                //     as explicit reference conversions. If `T` is not known to be a reference type,
                //     the conversions above are classified as unboxing conversions.
                //
                // To implement this spec excerpt, we simply test if the environment equivalent
                // of the target type is a subtype of the environment equivalent of the source
                // type. We also make sure that the source type is a reference type and the
                // target type a value type.
                return ConversionDescription.UnboxValueConversion;
            }

            if (IsCSharpReferenceType(envSourceType) && IsCSharpReferenceType(envTargetType))
            {
                if (HasImplicitReferenceConversion(envSourceType, envTargetType))
                {
                    // Implicit reference conversion.
                    return ConversionDescription.ReinterpretCast;
                }
                else if (HasExplicitReferenceConversion(envSourceType, envTargetType))
                {
                    // Downcast.
                    return ConversionDescription.DynamicCast;
                }
                else
                {
                    return NoConversion(envSourceType, envTargetType);
                }
            }
            else if (ConversionExpression.Instance.UseExplicitBox(envSourceType, envTargetType))
            {
                // Boxing conversion.
                if (envSourceType.Is(envTargetType))
                {
                    return ConversionDescription.ImplicitBoxingConversion;
                }
                else if (envSourceType.GetIsGenericParameter())
                {
                    return ConversionDescription.ExplicitBoxingConversion;
                }
            }

            // This is what the C# spec has to say about pointer conversions:
            //
            //     In an unsafe context, the set of available implicit conversions (Implicit conversions)
            //     is extended to include the following implicit pointer conversions:
            //
            //         * From any pointer_type to the type void*.
            //         * From the null literal to any pointer_type.
            //
            //     Additionally, in an unsafe context, the set of available explicit conversions (Explicit
            //     conversions) is extended to include the following explicit pointer conversions:
            //
            //         * From any pointer_type to any other pointer_type.
            //         * From sbyte, byte, short, ushort, int, uint, long, or ulong to any pointer_type.
            //         * From any pointer_type to sbyte, byte, short, ushort, int, uint, long, or ulong.

            if (IsTransientPointer(SourceType))
            {
                if (IsTransientPointer(TargetType))
                {
                    if (TargetType.AsPointerType().ElementType == PrimitiveTypes.Void)
                    {
                        // From any pointer_type to the type void*. (implicit)
                        return ConversionDescription.ImplicitPointerCast;
                    }
                    else
                    {
                        // From any pointer_type to any other pointer_type. (explicit)
                        return ConversionDescription.ExplicitPointerCast;
                    }
                }
                else if (TargetType == PrimitiveTypes.Null)
                {
                    // From the null literal to any pointer_type. (implicit)
                    return ConversionDescription.ImplicitPointerCast;
                }
                else if (TargetType.GetIsInteger())
                {
                    // From any pointer_type to sbyte, byte, short, ushort, int, uint, long, or ulong. (explicit)
                    return ConversionDescription.ExplicitStaticCast;
                }
            }
            else if (IsTransientPointer(TargetType) && SourceType.GetIsInteger())
            {
                // From sbyte, byte, short, ushort, int, uint, long, or ulong to any pointer_type. (explicit)
                return ConversionDescription.ExplicitStaticCast;
            }

            return NoConversion(SourceType, TargetType);
        }

        /// <summary>
        /// Determines if there is an implicit reference conversion from the given source
        /// reference type to the specified target reference type.
        /// </summary>
        /// <returns>
        /// <c>true</c> if there is an implicit reference conversion from the given source
        /// type to the specified target type; otherwise, <c>false</c>.
        /// </returns>
        /// <param name="SourceReferenceType">The source reference type.</param>
        /// <param name="TargetReferenceType">The target reference type.</param>
        private static bool HasImplicitReferenceConversion(
            IType SourceReferenceType, IType TargetReferenceType)
        {
            // Quote from the C# spec:
            //
            // The implicit reference conversions are:
            //
            // *  From any reference_type to object and dynamic.
            // *  From any class_type S to any class_type T, provided S is derived from T.
            // *  From any class_type S to any interface_type T, provided S implements T.
            // *  From any interface_type S to any interface_type T, provided S is derived from T.
            // *  From an array_type S with an element type SE to an array_type T with an element
            //    type TE, provided all of the following are true:
            //     *  S and T differ only in element type. In other words, S and T have the same
            //        number of dimensions.
            //     *  Both SE and TE are reference_types.
            //     *  An implicit reference conversion exists from SE to TE.
            // *  From any array_type to System.Array and the interfaces it implements.
            // *  From a single-dimensional array type S[] to System.Collections.Generic.IList<T>
            //    and its base interfaces, provided that there is an implicit identity or
            //    reference conversion from S to T.
            // *  From any delegate_type to System.Delegate and the interfaces it implements.
            // *  From the null literal to any reference_type.
            // *  From any reference_type to a reference_type T if it has an implicit identity
            //    or reference conversion to a reference_type T0 and T0 has an identity conversion
            //    to T.
            // *  From any reference_type to an interface or delegate type T if it has an implicit
            //    identity or reference conversion to an interface or delegate type T0 and T0 is
            //    variance-convertible (Variance conversion) to T.
            // *  Implicit conversions involving type parameters that are known to be reference
            //    types. See Implicit conversions involving type parameters for more details
            //    on implicit conversions involving type parameters.
            //
            // The implicit reference conversions are those conversions between reference_types
            // that can be proven to always succeed, and therefore require no checks at run-time.
            //
            // Reference conversions, implicit or explicit, never change the referential identity
            // of the object being converted. In other words, while a reference conversion may
            // change the type of the reference, it never changes the type or value of the object
            // being referred to.

            if (TargetReferenceType.GetIsRootType())
            {
                return true;
            }
            else if (SourceReferenceType.GetIsArray())
            {
                var srcArr = SourceReferenceType.AsArrayType();
                if (TargetReferenceType.GetIsArray())
                {
                    var tgtArr = SourceReferenceType.AsArrayType();
                    return srcArr.ArrayRank == tgtArr.ArrayRank
                    && IsCSharpReferenceType(srcArr.ElementType)
                    && IsCSharpReferenceType(tgtArr.ElementType)
                    && HasImplicitReferenceConversion(
                        srcArr.ElementType, tgtArr.ElementType);
                }
                // TODO: implement the conversion rules listed below. They are somewhat
                // troublesome to implement because relying on specific type names
                // constrains us to a specific standard library. Ideally, we'd get this
                // information from the back-end in the future.
                //
                // *  From any array_type to System.Array and the interfaces it implements.
                // *  From a single-dimensional array type S[] to System.Collections.Generic.IList<T>
                //    and its base interfaces, provided that there is an implicit identity or
                //    reference conversion from S to T.
                //
                // HACK: allow conversions from S[] to IEnumerable<T> as a temporary fix.
                else if (srcArr.ArrayRank == 1
                    && TargetReferenceType.GetIsEnumerableType())
                {
                    var enumerableElemType = TargetReferenceType.GetEnumerableElementType();
                    return srcArr.ElementType.Equals(enumerableElemType)
                    || (IsCSharpReferenceType(srcArr.ElementType)
                    && IsCSharpReferenceType(enumerableElemType)
                    && HasImplicitReferenceConversion(srcArr.ElementType, enumerableElemType));
                }
                return false;
            }
            else if (SourceReferenceType.GetIsDelegate())
            {
                // TODO: implement the rule quoted below. Some reasoning as above.
                //
                // *  From any delegate_type to System.Delegate and the interfaces it implements.
                return false;
            }
            else if (SourceReferenceType.Equals(PrimitiveTypes.Null))
            {
                return true;
            }
            else
            {
                return SourceReferenceType.Is(TargetReferenceType);
            }
        }

        /// <summary>
        /// Determines if there is an explicit reference conversion from the given source
        /// reference type to the specified target reference type.
        /// </summary>
        /// <returns>
        /// <c>true</c> if there is an explicit reference conversion from the given source
        /// type to the specified target type; otherwise, <c>false</c>.
        /// </returns>
        /// <param name="SourceReferenceType">The source reference type.</param>
        /// <param name="TargetReferenceType">The target reference type.</param>
        private static bool HasExplicitReferenceConversion(
            IType SourceReferenceType, IType TargetReferenceType)
        {
            // Let's hear what the C# spec has to say about this:
            //
            // The explicit reference conversions are:
            // *  From object and dynamic to any other reference_type.
            // *  From any class_type S to any class_type T, provided S is a base class of T.
            // *  From any class_type S to any interface_type T, provided S is not sealed and
            //    provided S does not implement T.
            // *  From any interface_type S to any class_type T, provided T is not sealed or
            //    provided T implements S.
            // *  From any interface_type S to any interface_type T, provided S is not derived
            //    from T.
            // *  From an array_type S with an element type SE to an array_type T with an element
            //    type TE, provided all of the following are true:
            //     *  S and T differ only in element type. In other words, S and T have the same
            //        number of dimensions.
            //     *  Both SE and TE are reference_types.
            //     *  An explicit reference conversion exists from SE to TE.
            // *  From System.Array and the interfaces it implements to any array_type.
            // *  From a single-dimensional array type S[] to System.Collections.Generic.IList<T>
            //    and its base interfaces, provided that there is an explicit reference conversion
            //    from S to T.
            // *  From System.Collections.Generic.IList<S> and its base interfaces to a
            //    single-dimensional array type T[], provided that there is an explicit identity
            //    or reference conversion from S to T.
            // *  From System.Delegate and the interfaces it implements to any delegate_type.
            // *  From a reference type to a reference type T if it has an explicit reference
            //    conversion to a reference type T0 and T0 has an identity conversion T.
            // *  From a reference type to an interface or delegate type T if it has an explicit
            //    reference conversion to an interface or delegate type T0 and either T0 is
            //    variance-convertible to T or T is variance-convertible to T0 (Variance conversion).
            // *  From D<S1...Sn> to D<T1...Tn> where D<X1...Xn> is a generic delegate type,
            //    D<S1...Sn> is not compatible with or identical to D<T1...Tn>, and for each
            //    type parameter Xi of D the following holds:
            //     *  If Xi is invariant, then Si is identical to Ti.
            //     *  If Xi is covariant, then there is an implicit or explicit identity
            //        or reference conversion from Si to Ti.
            //     *  If Xi is contravariant, then Si and Ti are either identical or both reference types.
            // *  Explicit conversions involving type parameters that are known to be reference types.
            //    For more details on explicit conversions involving type parameters, see Explicit
            //    conversions involving type parameters.
            //
            // The explicit reference conversions are those conversions between reference-types that
            // require run-time checks to ensure they are correct.
            //
            // For an explicit reference conversion to succeed at run-time, the value of the source
            // operand must be null, or the actual type of the object referenced by the source operand
            // must be a type that can be converted to the destination type by an implicit reference
            // conversion (Implicit reference conversions) or boxing conversion (Boxing conversions).
            // If an explicit reference conversion fails, a System.InvalidCastException is thrown.
            //
            // Reference conversions, implicit or explicit, never change the referential identity of
            // the object being converted. In other words, while a reference conversion may change the
            // type of the reference, it never changes the type or value of the object being referred to.

            if (SourceReferenceType.GetIsRootType())
            {
                return true;
            }
            else if (SourceReferenceType.GetIsArray())
            {
                if (TargetReferenceType.GetIsArray())
                {
                    var srcArr = SourceReferenceType.AsArrayType();
                    var tgtArr = SourceReferenceType.AsArrayType();
                    return srcArr.ArrayRank == tgtArr.ArrayRank
                    && IsCSharpReferenceType(srcArr.ElementType)
                    && IsCSharpReferenceType(tgtArr.ElementType)
                    && HasExplicitReferenceConversion(
                        srcArr.ElementType, tgtArr.ElementType);
                }
                // TODO: implement the conversion rules listed below. They are somewhat
                // troublesome to implement because relying on specific type names
                // constrains us to a specific standard library. Ideally, we'd get this
                // information from the back-end in the future.
                //
                // *  From System.Array and the interfaces it implements to any array_type.
                // *  From a single-dimensional array type S[] to System.Collections.Generic.IList<T>
                //    and its base interfaces, provided that there is an explicit reference conversion
                //    from S to T.
                // *  From System.Collections.Generic.IList<S> and its base interfaces to a
                //    single-dimensional array type T[], provided that there is an explicit identity
                //    or reference conversion from S to T.
                return false;
            }
            // HACK: allow conversions from IEnumerable<S> to T[] as a temporary fix.
            else if (SourceReferenceType.GetIsEnumerableType()
                     && TargetReferenceType.GetIsArray())
            {
                var enumerableElemType = SourceReferenceType.GetEnumerableElementType();
                var tgtArr = TargetReferenceType.AsArrayType();
                return tgtArr.ArrayRank == 1
                && (tgtArr.ElementType.Equals(enumerableElemType)
                    || (IsCSharpReferenceType(tgtArr.ElementType)
                        && IsCSharpReferenceType(enumerableElemType)
                        && (HasImplicitReferenceConversion(
                            enumerableElemType, tgtArr.ElementType)
                            || HasExplicitReferenceConversion(
                                enumerableElemType, tgtArr.ElementType))));
            }
            else if (SourceReferenceType.GetIsDelegate())
            {
                return TargetReferenceType.GetIsDelegate()
                && HasExplicitGenericDelegateConversion(
                    SourceReferenceType, TargetReferenceType);
            }
            else if (SourceReferenceType.GetIsInterface())
            {
                if (TargetReferenceType.GetIsInterface())
                {
                    // We can relax the requirement that "S is not derived from T",
                    // because implicit reference conversions are always preferred
                    // over explicit reference conversions -- if S were derived from
                    // T, then `HasExplicitReferenceConversion` would never have been
                    // called in the first place.
                    return true;
                }
                else if (!TargetReferenceType.GetIsArray()
                         && !TargetReferenceType.GetIsDelegate())
                {
                    return TargetReferenceType.GetIsVirtual()
                    || TargetReferenceType.Is(SourceReferenceType);
                }
                else
                {
                    return false;
                }
            }
            else
            {
                if (TargetReferenceType.GetIsInterface())
                {
                    // We can relax the "and provided S does not implement T" requirement,
                    // because implicit reference conversions are always preferred
                    // over explicit reference conversions -- if S actually implemented
                    // T, then `HasExplicitReferenceConversion` would never have been
                    // called in the first place.
                    return SourceReferenceType.GetIsVirtual();
                }
                else if (!TargetReferenceType.GetIsArray()
                         && !TargetReferenceType.GetIsDelegate())
                {
                    return TargetReferenceType.Is(SourceReferenceType);
                }
                else
                {
                    return false;
                }
            }
        }

        private static bool HasExplicitGenericDelegateConversion(
            IType SourceDelegateType, IType TargetDelegateType)
        {
            // This function implements the following rule (from the C# spec):
            //
            // *  From D<S1...Sn> to D<T1...Tn> where D<X1...Xn> is a generic delegate type,
            //    D<S1...Sn> is not compatible with or identical to D<T1...Tn>, and for each
            //    type parameter Xi of D the following holds:
            //     *  If Xi is invariant, then Si is identical to Ti.
            //     *  If Xi is covariant, then there is an implicit or explicit identity
            //        or reference conversion from Si to Ti.
            //     *  If Xi is contravariant, then Si and Ti are either identical or both reference types.

            if (!SourceDelegateType.GetGenericDeclaration().Equals(
                TargetDelegateType.GetGenericDeclaration()))
            {
                return false;
            }

            var srcArgs = SourceDelegateType.GetGenericArguments().ToArray();

            if (srcArgs.Length == 0)
                return false;

            var tgtArgs = TargetDelegateType.GetGenericArguments().ToArray();
            var srcParams = SourceDelegateType.GetGenericDeclaration().GenericParameters.ToArray();

            for (int i = 0; i < srcArgs.Length; i++)
            {
                var genParam = srcParams[i];
                var srcGenArg = srcArgs[i];
                var tgtGenArg = tgtArgs[i];

                bool foundMatch = srcGenArg.Equals(tgtGenArg)
                    || (genParam.GetIsCovariant()
                        && (HasImplicitReferenceConversion(
                            srcGenArg, tgtGenArg)
                            || HasExplicitReferenceConversion(
                                srcGenArg, tgtGenArg))
                    || (genParam.GetIsContravariant()
                        && IsCSharpReferenceType(srcGenArg)
                        && IsCSharpReferenceType(tgtGenArg)));

                if (!foundMatch)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Classifies a user-defined conversion of the source type
        /// to the target type.
        /// </summary>
        /// <returns>The user defined conversion.</returns>
        /// <param name="SourceType">The source type.</param>
        /// <param name="TargetType">The target type.</param>
        /// <param name="Scope">The scope in which the conversion is performed.</param>
        /// <param name="IsExplicit">Specifies if an explicit conversion is performed.</param>
        private IReadOnlyList<ConversionDescription> ClassifyUserDefinedConversion(
            IType SourceType, IType TargetType,
            FunctionScope Scope, bool IsExplicit)
        {
            // Find candidate operators.
            var candidates = GetUserDefinedConversionCandidates(
                SourceType, TargetType, Scope, IsExplicit);

            if (candidates.Count == 0)
                // We found nothing. Early-out here.
                return candidates;

            // Find the most specific source type.
            var srcType = IsExplicit
                ? GetMostSpecificSourceTypeExplicit(SourceType, candidates)
                : GetMostSpecificSourceTypeImplicit(SourceType, candidates);

            if (srcType == null)
                return new ConversionDescription[] { };

            // Find the most specific target type.
            var tgtType = IsExplicit
                ? GetMostSpecificTargetTypeExplicit(TargetType, candidates)
                : GetMostSpecificTargetTypeImplicit(TargetType, candidates);

            if (tgtType == null)
                return new ConversionDescription[] { };

            // Find the most specific conversion.
            return PickMostSpecificConversions(srcType, tgtType, candidates);
        }

        private IType GetMostSpecificSourceTypeImplicit(
            IType SourceType,
            IReadOnlyList<UserDefinedConversionDescription> Conversions)
        {
            // Quote from the spec for implicit user-defined conversions:
            //
            // Find the most specific source type, SX, of the operators in U:
            //
            //     * If any of the operators in U convert from S, then SX is S.
            //
            //     * Otherwise, SX is the most encompassed type in the combined set of source
            //       types of the operators in U. If exactly one most encompassed type cannot
            //       be found, then the conversion is ambiguous and a compile-time error occurs.
            //

            var paramTypes = Conversions.Select(
                conv => UserDefinedConversionDescription
                    .GetConversionMethodParameterType(conv.ConversionMethod))
                .ToArray();

            if (paramTypes.Contains(SourceType))
                return SourceType;
            else
                return GetMostEncompassedType(paramTypes);
        }

        private IType GetMostSpecificSourceTypeExplicit(
            IType SourceType,
            IReadOnlyList<UserDefinedConversionDescription> Conversions)
        {
            // Quote from the spec for explicit user-defined conversions:
            //
            // Find the most specific source type, SX, of the operators in U:
            //
            //     * If any of the operators in U convert from S, then SX is S.
            //
            //     * Otherwise, if any of the operators in U convert from types that encompass S,
            //       then SX is the most encompassed type in the combined set of source types of
            //       those operators. If no most encompassed type can be found, then the conversion
            //       is ambiguous and a compile-time error occurs.
            //
            //     * Otherwise, SX is the most encompassing type in the combined set of source
            //       types of the operators in U. If exactly one most encompassing type cannot be
            //       found, then the conversion is ambiguous and a compile-time error occurs.

            var paramTypes = Conversions.Select(
                conv => UserDefinedConversionDescription
                    .GetConversionMethodParameterType(conv.ConversionMethod))
                .ToArray();

            if (paramTypes.Contains(SourceType))
                return SourceType;
            else if (paramTypes.Any(type => IsEncompassedBy(SourceType, type)))
                return GetMostEncompassedType(paramTypes);
            else
                return GetMostEncompassingType(paramTypes);
        }

        private IType GetMostSpecificTargetTypeImplicit(
            IType TargetType,
            IReadOnlyList<UserDefinedConversionDescription> Conversions)
        {
            // Quote from the spec for implicit user-defined conversions:
            //
            // Find the most specific target type, TX, of the operators in U:
            //
            //     * If any of the operators in U convert to T, then TX is T.
            //
            //     * Otherwise, TX is the most encompassing type in the combined set of
            //       target types of the operators in U. If exactly one most encompassing
            //       type cannot be found, then the conversion is ambiguous and a
            //       compile-time error occurs.
            //

            var retTypes = Conversions.Select(conv => conv.ConversionMethod.ReturnType)
                .ToArray();

            if (retTypes.Contains(TargetType))
                return TargetType;
            else
                return GetMostEncompassingType(retTypes);
        }

        private IType GetMostSpecificTargetTypeExplicit(
            IType TargetType,
            IReadOnlyList<UserDefinedConversionDescription> Conversions)
        {
            // Quote from the spec for explicit user-defined conversions:
            //
            // Find the most specific target type, TX, of the operators in U:
            //
            //     * If any of the operators in U convert to T, then TX is T.
            //
            //     * Otherwise, if any of the operators in U convert to types that are
            //       encompassed by T, then TX is the most encompassing type in the
            //       combined set of target types of those operators. If exactly one most
            //       encompassing type cannot be found, then the conversion is ambiguous
            //       and a compile-time error occurs.
            //
            //     * Otherwise, TX is the most encompassed type in the combined set of
            //       target types of the operators in U. If no most encompassed type can
            //       be found, then the conversion is ambiguous and a compile-time error occurs.
            //


            var retTypes = Conversions.Select(conv => conv.ConversionMethod.ReturnType)
                .ToArray();

            if (retTypes.Contains(TargetType))
                return TargetType;
            else if (retTypes.Any(type => IsEncompassedBy(type, TargetType)))
                return GetMostEncompassingType(retTypes);
            else
                return GetMostEncompassedType(retTypes);
        }

        private static IReadOnlyList<UserDefinedConversionDescription> PickMostSpecificConversions(
            IType MostSpecificSourceType, IType MostSpecificTargetType,
            IReadOnlyList<UserDefinedConversionDescription> Conversions)
        {
            // Quote from the spec:
            //
            // Find the most specific conversion operator:
            //
            //     * If U contains exactly one user-defined conversion operator that
            //       converts from SX to TX, then this is the most specific conversion
            //       operator.
            //
            //     * Otherwise, if U contains exactly one lifted conversion operator
            //       that converts from SX to TX, then this is the most specific
            //       conversion operator.
            //
            //     * Otherwise, the conversion is ambiguous and a compile-time error
            //       occurs.
            //

            var results = new List<UserDefinedConversionDescription>();
            foreach (var conv in Conversions)
            {
                var paramTy = UserDefinedConversionDescription
                    .GetConversionMethodParameterType(conv.ConversionMethod);

                if (paramTy.Equals(MostSpecificSourceType)
                    && conv.ConversionMethod.ReturnType.Equals(MostSpecificTargetType))
                {
                    results.Add(conv);
                }
            }
            return results;
        }

        private IReadOnlyList<UserDefinedConversionDescription> GetUserDefinedConversionCandidates(
            IType SourceType, IType TargetType,
            FunctionScope Scope, bool IsExplicit)
        {
            // This method implements the following piece of logic for implicit conversions
            // (quote from the spec):
            //
            //     * "Find the set of types, D, from which user-defined conversion operators
            //       will be considered. This set consists of
            //         - S0 (if S0 is a class or struct),
            //         - the base classes of S0 (if S0 is a class), and
            //         - T0 (if T0 is a class or struct).
            //
            //     * Find the set of applicable user-defined and lifted conversion operators, U.
            //       This set consists of the user-defined and lifted implicit conversion operators
            //       declared by the classes or structs in D that convert from a type encompassing
            //       S to a type encompassed by T. If U is empty, the conversion is undefined and
            //       a compile-time error occurs."
            //
            // For explicit conversions, the equivalent excerpt from the spec is:
            //
            //     * "Find the set of types, D, from which user-defined conversion operators
            //       will be considered. This set consists of
            //         - S0 (if S0 is a class or struct),
            //         - the base classes of S0 (if S0 is a class),
            //         - T0 (if T0 is a class or struct), and
            //         - the base classes of T0 (if T0 is a class).
            //
            //     * Find the set of applicable user-defined and lifted conversion operators, U.
            //       This set consists of the user-defined and lifted implicit or explicit
            //       conversion operators declared by the classes or structs in D that convert
            //       from a type encompassing or encompassed by S to a type encompassing or
            //       encompassed by T. If U is empty, the conversion is undefined and a
            //       compile-time error occurs."
            //
            // So, the three differences are:
            //
            //     1. Explicit conversions do consider base classes of T0, but
            //        implicit conversions do not.
            //
            //     2. Explicit conversions consider both explicit and implicit
            //        operators. Implicit conversions consider only implicit
            //        operators.
            //
            //     3. For implicit conversions, operators may only convert from
            //        a type encompassing S to a type encompassed by T. For
            //        explicit conversions, operators may from a type encompassing
            //        or encompassed by S to a type encompassing or
            //        encompassed by T.
            //
            // This method implements the spec by doing the following:
            //
            //     * First, find all conversion operators defined by types in D
            //       that have the correct implicit/explicitness.
            //
            //     * Then, filter these operators by their parameters/return types.
            //

            // Scope.GetOperators corresponds to finding all operators of a given kind
            // defined by either the given type or one of its base classes.
            var potentialResults = new HashSet<IMethod>();
            potentialResults.UnionWith(Scope.GetOperators(SourceType, Operator.ConvertImplicit));

            if (IsExplicit)
            {
                potentialResults.UnionWith(Scope.GetOperators(SourceType, Operator.ConvertExplicit));

                // For explicit operators, we can use Scope.GetOperators again for
                // the target type.
                potentialResults.UnionWith(Scope.GetOperators(TargetType, Operator.ConvertImplicit));
                potentialResults.UnionWith(Scope.GetOperators(TargetType, Operator.ConvertExplicit));
            }
            else
            {
                // For implicit operators, we'll need to search through its methods
                // (which is unfortunately less efficient).
                potentialResults.UnionWith(TargetType.GetOperatorMethods(Operator.ConvertImplicit));
            }

            // Now, we'll filter our results and construct candidate
            // conversions.
            var filteredCandidates = new List<UserDefinedConversionDescription>();
            foreach (var item in potentialResults)
            {
                var candidate = CreateUserDefinedConversionCandidate(
                    item, IsExplicit, SourceType, TargetType, Scope);

                if (candidate != null)
                    filteredCandidates.Add(candidate);
            }

            return filteredCandidates;
        }

        /// <summary>
        /// Creates a candidate user-defined conversion description
        /// from the given candidate user-defined conversion operator,
        /// source type and target type.
        /// </summary>
        /// <returns>The user defined conversion candidate.</returns>
        /// <param name="CandidateMethod">
        /// The candidate user-defined conversion operator.
        /// </param>
        /// <param name="IsExplicit">
        /// Specifies if an explicit conversion candidate is to be created.
        /// </param>
        /// <param name="From">The source type.</param>
        /// <param name="To">The target type.</param>
        /// <param name="Scope">The enclosing scope.</param>
        private UserDefinedConversionDescription CreateUserDefinedConversionCandidate(
            IMethod CandidateMethod, bool IsExplicit,
            IType From, IType To, FunctionScope Scope)
        {
            if (CandidateMethod.IsStatic)
            {
                if (CandidateMethod.Parameters.Count() != 1)
                    // Static methods with empty parameter lists of
                    // non-unit length are not acceptable as
                    // user-defined conversion methods.
                    return null;
            }
            else if (CandidateMethod.Parameters.Any())
            {
                // Instance methods with non-empty parameter lists are not
                // acceptable as user-defined conversion methods.
                return null;
            }

            var methodParamType = UserDefinedConversionDescription
                .GetConversionMethodParameterType(CandidateMethod);

            var fromConv = IsExplicit
                ? GetStandardConversion(From, methodParamType)
                : GetEncompassingConversion(From, methodParamType);

            if (!fromConv.Exists)
                return null;

            var toConv = IsExplicit
                ? GetStandardConversion(CandidateMethod.ReturnType, To)
                : GetEncompassingConversion(CandidateMethod.ReturnType, To);

            if (!toConv.Exists)
                return null;

            if (IsExplicit)
                return ConversionDescription.ExplicitUserDefined(
                    CandidateMethod, fromConv, toConv);
            else
                return ConversionDescription.ImplicitUserDefined(
                    CandidateMethod, fromConv, toConv);
        }

        /// <summary>
        /// Tests if the first type is encompassed by the second.
        /// If that is the case, then an implicit standard
        /// conversion is returned from the first type to
        /// the second type.
        /// </summary>
        /// <param name="Encompasser">The first type.</param>
        /// <param name="Encompassed">The second type.</param>
        private ConversionDescription GetEncompassingConversion(
            IType From, IType To)
        {
            // The spec says that
            //
            // "If a standard implicit conversion (Standard implicit conversions)
            // exists from a type A to a type B, and if neither A nor B are
            // interface_types, then A is said to be encompassed by B, and
            // B is said to encompass A."
            //
            // This function implements that behavior like so:
            //
            //     1. Check if either type is an interface. This
            //        is cheap and allows us to early-out.
            //
            //     2. Classify the conversion without considering
            //        user-defined conversions, then test if we
            //        can find an implicit conversion.

            if (From.GetIsInterface() || To.GetIsInterface())
                return ConversionDescription.None;

            var conv = ClassifyBuiltinConversion(From, To);

            if (conv.IsImplicit)
                return conv;
            else
                return ConversionDescription.None;
        }

        /// <summary>
        /// Gets a standard conversion from the first type
        /// to the second, if either type encompasses the other.
        /// </summary>
        /// <returns>The standard conversion.</returns>
        /// <param name="From">The type to convert from.</param>
        /// <param name="To">The type to convert to.</param>
        private ConversionDescription GetStandardConversion(
            IType From, IType To)
        {
            var implicitConv = GetEncompassingConversion(From, To);
            if (implicitConv.Exists)
                return implicitConv;

            var explicitInvConv = GetEncompassingConversion(To, From);
            if (explicitInvConv.Exists)
                return InvertImplicitStandardConversion(explicitInvConv);
            else
                return ConversionDescription.None;
        }

        /// <summary>
        /// Inverts the given implicit standard conversion.
        /// </summary>
        /// <returns>An explicit standard conversion.</returns>
        /// <param name="Description">The standard conversion to invert.</param>
        private static ConversionDescription InvertImplicitStandardConversion(
            ConversionDescription Description)
        {
            switch (Description.Kind)
            {
                case ConversionKind.Identity:
                    return Description;
                case ConversionKind.ImplicitBoxingConversion:
                    return ConversionDescription.UnboxValueConversion;
                case ConversionKind.NumberToEnumStaticCast:
                    return ConversionDescription.EnumToNumberStaticCast;
                case ConversionKind.EnumToNumberStaticCast:
                    return ConversionDescription.NumberToEnumStaticCast;
                case ConversionKind.EnumToEnumStaticCast:
                    return ConversionDescription.EnumToEnumStaticCast;
                case ConversionKind.ReinterpretCast:
                    return ConversionDescription.DynamicCast;
                default:
                    throw new InvalidOperationException("Cannot invert conversion kind '" + Description.Kind + "'");
            }
        }

        /// <summary>
        /// Gets the most encompassed type in the given set,
        /// or null if there is no such type.
        /// </summary>
        /// <returns>The most encompassed type.</returns>
        /// <param name="Types">The set of types to search for a most encompassed type.</param>
        private IType GetMostEncompassedType(IEnumerable<IType> Types)
        {
            // Quote from the spec:
            //
            // The most encompassed type in a set of types is the one type that is
            // encompassed by all other types in the set. If no single type is
            // encompassed by all other types, then the set has no most encompassed type.
            // In more intuitive terms, the most encompassed type is the "smallest" type
            // in the set—the one type that can be implicitly converted to each of the
            // other types.
            //

            return Types.GetBestElementOrDefault(Encompassed);
        }

        /// <summary>
        /// Gets the most encompassing type in the given set,
        /// or null if there is no such type.
        /// </summary>
        /// <returns>The most encompassing type.</returns>
        /// <param name="Types">The set of types to search for a most encompassing type.</param>
        private IType GetMostEncompassingType(IEnumerable<IType> Types)
        {
            // Quote from the spec:
            //
            // The most encompassing type in a set of types is the one type that
            // encompasses all other types in the set. If no single type encompasses
            // all other types, then the set has no most encompassing type. In more
            // intuitive terms, the most encompassing type is the "largest" type in
            // the set—the one type to which each of the other types can be
            // implicitly converted.
            //

            return Types.GetBestElementOrDefault(Encompasses);
        }

        /// <summary>
        /// Determines if the first type is encompassed by the second.
        /// </summary>
        /// <returns><c>true</c> if the first type is encompassed by the second; otherwise, <c>false</c>.</returns>
        /// <param name="Encompassed">The first type.</param>
        /// <param name="Encompasser">The second type.</param>
        private bool IsEncompassedBy(IType Encompassed, IType Encompasser)
        {
            return GetEncompassingConversion(Encompassed, Encompasser).Exists;
        }

        /// <summary>
        /// Tests which of the given types is encompassed by the other.
        /// </summary>
        /// <param name="First">The first type.</param>
        /// <param name="Second">The second type.</param>
        private Betterness Encompassed(IType First, IType Second)
        {
            bool firstEncompassedBySecond = IsEncompassedBy(First, Second);
            bool secondEncompassedByFirst = IsEncompassedBy(Second, First);
            if (firstEncompassedBySecond)
            {
                if (secondEncompassedByFirst)
                    return Betterness.Equal;
                else
                    return Betterness.First;
            }
            else
            {
                if (secondEncompassedByFirst)
                    return Betterness.Second;
                else
                    return Betterness.Neither;
            }
        }

        /// <summary>
        /// Tests which of the given types encompasses the other.
        /// </summary>
        /// <param name="First">The first type.</param>
        /// <param name="Second">The second type.</param>
        private Betterness Encompasses(IType First, IType Second)
        {
            return Encompassed(First, Second).Flip();
        }

        private IReadOnlyList<ConversionDescription> ClassifyMethodGroupConversion(
            IEnumerable<IExpression> SourceSignatures, IMethod TargetSignature, FunctionScope Scope)
        {
            // The spec on method group conversions:
            //
            // An implicit conversion (Implicit conversions) exists from a method group (Expression classifications)
            // to a compatible delegate type. Given a delegate type `D` and an expression `E` that is
            // classified as a method group, an implicit conversion exists from `E` to `D` if `E` contains
            // at least one method that is applicable in its normal form (Applicable function member) to an argument
            // list constructed by use of the parameter types and modifiers of `D`, as described in the following.
            // The compile-time application of a conversion from a method group `E` to a delegate type `D` is
            // described in the following. Note that the existence of an implicit conversion from `E` to `D` does
            // not guarantee that the compile-time application of the conversion will succeed without error.
            //
            // *  A single method `M` is selected corresponding to a method invocation (Method invocations)
            //    of the form `E(A)`, with the following modifications:
            //
            //     * The argument list `A` is a list of expressions, each classified as a variable and with
            //       the type and modifier (`ref` or `out`) of the corresponding parameter in the
            //       *formal_parameter_list* of `D`.
            //
            //     * The candidate methods considered are only those methods that are applicable in their
            //       normal form (Applicable function member), not those applicable only in their expanded
            //       form.
            //
            // *  If the algorithm of Method invocations produces an error, then a compile-time error occurs.
            //    Otherwise the algorithm produces a single best method `M` having the same number of parameters
            //    as `D` and the conversion is considered to exist.
            //
            // *  The selected method `M` must be compatible (Delegate compatibility) with the delegate type `D`,
            //    or otherwise, a compile-time error occurs.
            //
            // *  If the selected method `M` is an instance method, the instance expression associated with `E`
            //    determines the target object of the delegate.
            //
            // *  If the selected method M is an extension method which is denoted by means of a member access
            //    on an instance expression, that instance expression determines the target object of the delegate.
            //
            // *  The result of the conversion is a value of type `D`, namely a newly created delegate that refers
            //    to the selected method and target object.
            //
            // *  Note that this process can lead to the creation of a delegate to an extension method, if the
            //    algorithm of Method invocations fails to find an instance method but succeeds in processing the
            //    invocation of `E(A)` as an extension method invocation (Extension method invocations).
            //    A delegate thus created captures the extension method as well as its first argument.

            var methodOverloads = new List<CandidateOverload>();
            foreach (var item in SourceSignatures)
            {
                NormalFormOverload overload;
                if (NormalFormOverload.TryCreate(item, out overload))
                {
                    methodOverloads.Add(overload);
                }
            }

            CandidateOverload resultOverload;
            if (!OverloadResolution.TryResolveOverload(
                methodOverloads,
                TargetSignature.Parameters
                    .Select(param => new UnknownExpression(param.ParameterType))
                    .ToArray(),
                Scope,
                out resultOverload))
            {
                return new ConversionDescription[] { };
            }

            var delegExpr = ((NormalFormOverload)resultOverload).DelegateExpression;
            var delegMethod = MethodType.GetMethod(delegExpr.Type);

            if (!IsCompatibleDelegate(delegMethod, TargetSignature))
            {
                return new ConversionDescription[] { };
            }

            return new ConversionDescription[] { ConversionDescription.ImplicitMethodGroup(delegMethod) };
        }

        /// <summary>
        /// Tests if a delegate with the first signature is compatible with the second signature.
        /// </summary>
        /// <param name="CompatibleSignature">
        /// The signature of the delegate value whose compatiblity with the reference delegate type is examined.</param>
        /// <param name="ReferenceSignature">The signature of the reference delegate type.</param>
        /// <returns><c>true</c> if the first signature is compatible with the second; otherwise, <c>false</c>.</returns>
        public bool IsCompatibleDelegate(IMethod CompatibleSignature, IMethod ReferenceSignature)
        {
            // According to the spec,
            //
            // A method or delegate `M` is ***compatible*** with a delegate type `D` if all
            // of the following are true:
            //
            // *  `D` and `M` have the same number of parameters, and each parameter in `D` has
            //    the same `ref` or `out` modifiers as the corresponding parameter in `M`.
            //
            // *  For each value parameter (a parameter with no `ref` or `out` modifier), an
            //    identity conversion (Identity conversion) or implicit reference conversion
            //    (Implicit reference conversions) exists from the parameter type in `D` to
            //    the corresponding parameter type in `M`.
            //
            // *  For each `ref` or `out` parameter, the parameter type in `D` is the same as
            //    the parameter type in `M`.
            //
            // *  An identity or implicit reference conversion exists from the return type of
            //    `M` to the return type of `D`.

            var firstParamList = CompatibleSignature.GetParameters();
            var secondParamList = ReferenceSignature.GetParameters();

            if (firstParamList.Length != secondParamList.Length)
                return false;

            for (int i = 0; i < firstParamList.Length; i++)
            {
                var firstType = firstParamList[i].ParameterType;
                var secondType = secondParamList[i].ParameterType;
                if (IsReferencePointer(firstType) || IsReferencePointer(secondType))
                {
                    if (!firstType.IsEquivalent(secondType))
                    {
                        return false;
                    }
                }
                else
                {
                    var paramConv = ClassifyBuiltinConversion(secondType, firstType);
                    if (paramConv.Kind != ConversionKind.Identity
                        && paramConv.Kind != ConversionKind.ReinterpretCast)
                    {
                        return false;
                    }
                }
            }

            var retValConv = ClassifyBuiltinConversion(
                CompatibleSignature.ReturnType, ReferenceSignature.ReturnType);

            return retValConv.Kind == ConversionKind.Identity
                || retValConv.Kind == ConversionKind.ReinterpretCast;
        }

        /// <summary>
        /// Tests if the given type is a reference pointer type.
        /// </summary>
        /// <param name="Type">The type to test for reference-pointerness.</param>
        /// <returns><c>true</c> if the given type is a reference pointer type; otherwise, <c>false</c>.</returns>
        public static bool IsReferencePointer(IType Type)
        {
            return Type.GetIsPointer() && Type.AsPointerType().PointerKind.Equals(PointerKind.ReferencePointer);
        }

        /// <summary>
        /// Tests if the given type is a transient pointer type.
        /// </summary>
        /// <param name="Type">The type to test for transient-pointerness.</param>
        /// <returns><c>true</c> if the given type is a transient pointer type; otherwise, <c>false</c>.</returns>
        public static bool IsTransientPointer(IType Type)
        {
            return Type.GetIsPointer() && Type.AsPointerType().PointerKind.Equals(PointerKind.TransientPointer);
        }

        private static readonly Dictionary<KeyValuePair<IType, IType>, ConversionDescription> primitiveConversions =
            new Dictionary<KeyValuePair<IType, IType>, ConversionDescription>()
            {
                // string -> T
                { new KeyValuePair<IType, IType>(PrimitiveTypes.String, PrimitiveTypes.Boolean), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.String, PrimitiveTypes.String), ConversionDescription.Identity },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.String, PrimitiveTypes.Char), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.String, PrimitiveTypes.Int8), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.String, PrimitiveTypes.Int16), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.String, PrimitiveTypes.Int32), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.String, PrimitiveTypes.Int64), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.String, PrimitiveTypes.UInt8), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.String, PrimitiveTypes.UInt16), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.String, PrimitiveTypes.UInt32), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.String, PrimitiveTypes.UInt64), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.String, PrimitiveTypes.Float32), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.String, PrimitiveTypes.Float64), ConversionDescription.None },

                // bool -> T
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Boolean, PrimitiveTypes.Boolean), ConversionDescription.Identity },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Boolean, PrimitiveTypes.String), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Boolean, PrimitiveTypes.Char), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Boolean, PrimitiveTypes.Int8), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Boolean, PrimitiveTypes.Int16), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Boolean, PrimitiveTypes.Int32), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Boolean, PrimitiveTypes.Int64), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Boolean, PrimitiveTypes.UInt8), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Boolean, PrimitiveTypes.UInt16), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Boolean, PrimitiveTypes.UInt32), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Boolean, PrimitiveTypes.UInt64), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Boolean, PrimitiveTypes.Float32), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Boolean, PrimitiveTypes.Float64), ConversionDescription.None },

                // char -> T
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Char, PrimitiveTypes.Boolean), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Char, PrimitiveTypes.String), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Char, PrimitiveTypes.Char), ConversionDescription.Identity },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Char, PrimitiveTypes.Int8), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Char, PrimitiveTypes.Int16), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Char, PrimitiveTypes.Int32), ConversionDescription.ImplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Char, PrimitiveTypes.Int64), ConversionDescription.ImplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Char, PrimitiveTypes.UInt8), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Char, PrimitiveTypes.UInt16), ConversionDescription.ImplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Char, PrimitiveTypes.UInt32), ConversionDescription.ImplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Char, PrimitiveTypes.UInt64), ConversionDescription.ImplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Char, PrimitiveTypes.Float32), ConversionDescription.ImplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Char, PrimitiveTypes.Float64), ConversionDescription.ImplicitStaticCast },

                // int8 -> T
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int8, PrimitiveTypes.Boolean), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int8, PrimitiveTypes.String), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int8, PrimitiveTypes.Char), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int8, PrimitiveTypes.Int8), ConversionDescription.Identity },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int8, PrimitiveTypes.Int16), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int8, PrimitiveTypes.Int32), ConversionDescription.ImplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int8, PrimitiveTypes.Int64), ConversionDescription.ImplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int8, PrimitiveTypes.UInt8), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int8, PrimitiveTypes.UInt16), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int8, PrimitiveTypes.UInt32), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int8, PrimitiveTypes.UInt64), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int8, PrimitiveTypes.Float32), ConversionDescription.ImplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int8, PrimitiveTypes.Float64), ConversionDescription.ImplicitStaticCast },

                // int16 -> T
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int16, PrimitiveTypes.Boolean), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int16, PrimitiveTypes.String), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int16, PrimitiveTypes.Char), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int16, PrimitiveTypes.Int8), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int16, PrimitiveTypes.Int16), ConversionDescription.Identity },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int16, PrimitiveTypes.Int32), ConversionDescription.ImplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int16, PrimitiveTypes.Int64), ConversionDescription.ImplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int16, PrimitiveTypes.UInt8), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int16, PrimitiveTypes.UInt16), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int16, PrimitiveTypes.UInt32), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int16, PrimitiveTypes.UInt64), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int16, PrimitiveTypes.Float32), ConversionDescription.ImplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int16, PrimitiveTypes.Float64), ConversionDescription.ImplicitStaticCast },

                // int32 -> T
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int32, PrimitiveTypes.Boolean), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int32, PrimitiveTypes.String), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int32, PrimitiveTypes.Char), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int32, PrimitiveTypes.Int8), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int32, PrimitiveTypes.Int16), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int32, PrimitiveTypes.Int32), ConversionDescription.Identity },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int32, PrimitiveTypes.Int64), ConversionDescription.ImplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int32, PrimitiveTypes.UInt8), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int32, PrimitiveTypes.UInt16), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int32, PrimitiveTypes.UInt32), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int32, PrimitiveTypes.UInt64), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int32, PrimitiveTypes.Float32), ConversionDescription.ImplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int32, PrimitiveTypes.Float64), ConversionDescription.ImplicitStaticCast },

                // int64 -> T
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int64, PrimitiveTypes.Boolean), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int64, PrimitiveTypes.String), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int64, PrimitiveTypes.Char), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int64, PrimitiveTypes.Int8), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int64, PrimitiveTypes.Int16), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int64, PrimitiveTypes.Int32), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int64, PrimitiveTypes.Int64), ConversionDescription.Identity },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int64, PrimitiveTypes.UInt8), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int64, PrimitiveTypes.UInt16), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int64, PrimitiveTypes.UInt32), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int64, PrimitiveTypes.UInt64), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int64, PrimitiveTypes.Float32), ConversionDescription.ImplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Int64, PrimitiveTypes.Float64), ConversionDescription.ImplicitStaticCast },

                // uint8 -> T
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt8, PrimitiveTypes.Boolean), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt8, PrimitiveTypes.String), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt8, PrimitiveTypes.Char), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt8, PrimitiveTypes.Int8), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt8, PrimitiveTypes.Int16), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt8, PrimitiveTypes.Int32), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt8, PrimitiveTypes.Int64), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt8, PrimitiveTypes.UInt8), ConversionDescription.Identity },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt8, PrimitiveTypes.UInt16), ConversionDescription.ImplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt8, PrimitiveTypes.UInt32), ConversionDescription.ImplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt8, PrimitiveTypes.UInt64), ConversionDescription.ImplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt8, PrimitiveTypes.Float32), ConversionDescription.ImplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt8, PrimitiveTypes.Float64), ConversionDescription.ImplicitStaticCast },

                // uint16 -> T
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt16, PrimitiveTypes.Boolean), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt16, PrimitiveTypes.String), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt16, PrimitiveTypes.Char), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt16, PrimitiveTypes.Int8), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt16, PrimitiveTypes.Int16), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt16, PrimitiveTypes.Int32), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt16, PrimitiveTypes.Int64), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt16, PrimitiveTypes.UInt8), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt16, PrimitiveTypes.UInt16), ConversionDescription.Identity },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt16, PrimitiveTypes.UInt32), ConversionDescription.ImplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt16, PrimitiveTypes.UInt64), ConversionDescription.ImplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt16, PrimitiveTypes.Float32), ConversionDescription.ImplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt16, PrimitiveTypes.Float64), ConversionDescription.ImplicitStaticCast },

                // uint32 -> T
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt32, PrimitiveTypes.Boolean), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt32, PrimitiveTypes.String), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt32, PrimitiveTypes.Char), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt32, PrimitiveTypes.Int8), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt32, PrimitiveTypes.Int16), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt32, PrimitiveTypes.Int32), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt32, PrimitiveTypes.Int64), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt32, PrimitiveTypes.UInt8), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt32, PrimitiveTypes.UInt16), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt32, PrimitiveTypes.UInt32), ConversionDescription.Identity },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt32, PrimitiveTypes.UInt64), ConversionDescription.ImplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt32, PrimitiveTypes.Float32), ConversionDescription.ImplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt32, PrimitiveTypes.Float64), ConversionDescription.ImplicitStaticCast },

                // uint64 -> T
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt64, PrimitiveTypes.Boolean), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt64, PrimitiveTypes.String), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt64, PrimitiveTypes.Char), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt64, PrimitiveTypes.Int8), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt64, PrimitiveTypes.Int16), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt64, PrimitiveTypes.Int32), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt64, PrimitiveTypes.Int64), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt64, PrimitiveTypes.UInt8), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt64, PrimitiveTypes.UInt16), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt64, PrimitiveTypes.UInt32), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt64, PrimitiveTypes.UInt64), ConversionDescription.Identity },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt64, PrimitiveTypes.Float32), ConversionDescription.ImplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.UInt64, PrimitiveTypes.Float64), ConversionDescription.ImplicitStaticCast },

                // float32 -> T
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Float32, PrimitiveTypes.Boolean), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Float32, PrimitiveTypes.String), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Float32, PrimitiveTypes.Char), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Float32, PrimitiveTypes.Int8), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Float32, PrimitiveTypes.Int16), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Float32, PrimitiveTypes.Int32), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Float32, PrimitiveTypes.Int64), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Float32, PrimitiveTypes.UInt8), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Float32, PrimitiveTypes.UInt16), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Float32, PrimitiveTypes.UInt32), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Float32, PrimitiveTypes.UInt64), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Float32, PrimitiveTypes.Float32), ConversionDescription.Identity },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Float32, PrimitiveTypes.Float64), ConversionDescription.ImplicitStaticCast },

                // float64 -> T
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Float64, PrimitiveTypes.Boolean), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Float64, PrimitiveTypes.String), ConversionDescription.None },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Float64, PrimitiveTypes.Char), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Float64, PrimitiveTypes.Int8), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Float64, PrimitiveTypes.Int16), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Float64, PrimitiveTypes.Int32), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Float64, PrimitiveTypes.Int64), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Float64, PrimitiveTypes.UInt8), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Float64, PrimitiveTypes.UInt16), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Float64, PrimitiveTypes.UInt32), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Float64, PrimitiveTypes.UInt64), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Float64, PrimitiveTypes.Float32), ConversionDescription.ExplicitStaticCast },
                { new KeyValuePair<IType, IType>(PrimitiveTypes.Float64, PrimitiveTypes.Float64), ConversionDescription.Identity },
            };
    }
}

