﻿using System;
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
        private EcsConversionRules()
        {
        }

        public static readonly EcsConversionRules Instance = new EcsConversionRules();

        /// <summary>
        /// Classifies a conversion of the given expression to the given type.
        /// </summary>
        public override IReadOnlyList<ConversionDescription> ClassifyConversion(
            IExpression Source, IType Type, FunctionScope Scope)
        {
            var srcType = Source.Type;

            // TODO: special cases for literals.

            return ClassifyConversion(srcType, Type, Scope);
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
        public static ConversionDescription ClassifyBuiltinConversion(
            IType SourceType, IType TargetType)
        {
            if (SourceType.Equals(TargetType))
            {
                // Identity conversion.
                return ConversionDescription.Identity;
            }
            else if (PrimitiveTypes.Null.Equals(SourceType)
                && TargetType.GetIsReferenceType())
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

            if (SourceType.GetIsEnum() && IsNumberPrimitive(TargetType))
            {
                // enum-to-integer
                return ConversionDescription.EnumToNumberStaticCast;
            }
            else if (TargetType.GetIsEnum() && IsNumberPrimitive(SourceType))
            {
                // integer-to-enum
                return ConversionDescription.NumberToEnumStaticCast;
            }
            else if (ConversionExpression.Instance.UseUnboxValue(SourceType, TargetType))
            {
                return ConversionDescription.UnboxValueConversion;
            }
            else if (ConversionExpression.Instance.UseDynamicCast(SourceType, TargetType))
            {
                if (ConversionExpression.Instance.UseReinterpretAsDynamicCast(SourceType, TargetType))
                {
                    // Upcast. 
                    return ConversionDescription.ReinterpretCast;
                }
                else
                {
                    // Downcast. 
                    return ConversionDescription.DynamicCast;
                }
            }
            else if (ConversionExpression.Instance.UseExplicitBox(SourceType, TargetType))
            {
                // Boxing conversion.
                return SourceType.Is(TargetType)
                    ? ConversionDescription.ImplicitBoxingConversion
                    : ConversionDescription.ExplicitBoxingConversion;
            }

            // TODO: pointer conversions
            // TODO: method group conversions

            return NoConversion(SourceType, TargetType);
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
        private static IReadOnlyList<ConversionDescription> ClassifyUserDefinedConversion(
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

        private static IType GetMostSpecificSourceTypeImplicit(
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

        private static IType GetMostSpecificSourceTypeExplicit(
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

        private static IType GetMostSpecificTargetTypeImplicit(
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

        private static IType GetMostSpecificTargetTypeExplicit(
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

        private static IReadOnlyList<UserDefinedConversionDescription> GetUserDefinedConversionCandidates(
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
        private static UserDefinedConversionDescription CreateUserDefinedConversionCandidate(
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
        private static ConversionDescription GetEncompassingConversion(
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
        private static ConversionDescription GetStandardConversion(
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
        private static IType GetMostEncompassedType(IEnumerable<IType> Types)
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
        private static IType GetMostEncompassingType(IEnumerable<IType> Types)
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
        private static bool IsEncompassedBy(IType Encompassed, IType Encompasser)
        {
            return GetEncompassingConversion(Encompassed, Encompasser).Exists;
        }

        /// <summary>
        /// Tests which of the given types is encompassed by the other.
        /// </summary>
        /// <param name="First">The first type.</param>
        /// <param name="Second">The second type.</param>
        private static Betterness Encompassed(IType First, IType Second)
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
        private static Betterness Encompasses(IType First, IType Second)
        {
            return Encompassed(First, Second).Flip();
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

