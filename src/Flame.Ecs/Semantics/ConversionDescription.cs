using System;
using System.Linq;
using Flame.Compiler;
using Flame.Compiler.Expressions;
using Flame.Collections;

namespace Flame.Ecs.Semantics
{
    /// <summary>
    /// Defines an enumeration of possible conversion types.
    /// </summary>
    public enum ConversionKind
    {
        /// <summary>
        /// No conversion is possible.
        /// </summary>
        None,

        /// <summary>
        /// The identity conversion is applicable.
        /// </summary>
        Identity,

        /// <summary>
        /// An implicit static cast is used to perform
        /// this conversion.
        /// </summary>
        ImplicitStaticCast,

        /// <summary>
        /// An explicit static cast is used to perform
        /// this conversion.
        /// </summary>
        ExplicitStaticCast,

        /// <summary>
        /// An implicit boxing conversion is applicable.
        /// </summary>
        ImplicitBoxingConversion,

        /// <summary>
        /// An explicit boxing conversion is applicable.
        /// </summary>
        ExplicitBoxingConversion,

        /// <summary>
        /// A value-unboxing conversion is applicable.
        /// </summary>
        UnboxValueConversion,

        /// <summary>
        /// An explicit static cast is used to perform
        /// an enum-to-number conversion.
        /// </summary>
        EnumToNumberStaticCast,

        /// <summary>
        /// An explicit static cast is used to perform
        /// an number-to-enum conversion.
        /// </summary>
        NumberToEnumStaticCast,

        /// <summary>
        /// A dynamic cast is used to perform
        /// this conversion.
        /// </summary>
        DynamicCast,

        /// <summary>
        /// A reinterpret cast is used to perform this 
        /// conversion.
        /// </summary>
        ReinterpretCast,

        /// <summary>
        /// An implicit user-defined conversion is used to perform
        /// this conversion.
        /// </summary>
        ImplicitUserDefined,

        /// <summary>
        /// An explicit user-defined conversion is used to perform
        /// this conversion.
        /// </summary>
        ExplicitUserDefined
    }

    /// <summary>
    /// Describes a conversion.
    /// </summary>
    public abstract class ConversionDescription
    {
        /// <summary>
        /// Gets the kind of conversion that is performed.
        /// </summary>
        public abstract ConversionKind Kind { get; }

        /// <summary>
        /// Uses the information captured by this conversion
        /// description to convert the given expression to the
        /// given type.
        /// </summary>
        public abstract IExpression Convert(IExpression Value, IType TargetType);

        /// <summary>
        /// Gets a value indicating whether this conversion exists.
        /// </summary>
        /// <value><c>true</c> if this conversion exists; otherwise, <c>false</c>.</value>
        public bool Exists
        {
            get { return Kind != ConversionKind.None; }
        }

        /// <summary>
        /// Gets a value indicating whether this instance describes an implicit conversion.
        /// </summary>
        /// <value><c>true</c> if the conversion is implicit; otherwise, <c>false</c>.</value>
        public bool IsImplicit
        {
            get { return !IsExplicit; }
        }

        /// <summary>
        /// Gets a value indicating whether this instance describes a static conversion,
        /// which does not incur any run-time check.
        /// </summary>
        public bool IsStatic
        {
            get { return Kind != ConversionKind.DynamicCast && Kind != ConversionKind.UnboxValueConversion; }
        }

        /// <summary>
        /// Gets a value indicating whether this instance describes a reference conversion.
        /// </summary>
        public bool IsReference
        {
            get
            { 
                return Kind == ConversionKind.DynamicCast
                || Kind == ConversionKind.ReinterpretCast; 
            }
        }

        /// <summary>
        /// Gets a value indicating whether this conversion is boxing.
        /// </summary>
        /// <value><c>true</c> if this conversion is boxing; otherwise, <c>false</c>.</value>
        public bool IsBoxing
        {
            get
            {
                return Kind == ConversionKind.ImplicitBoxingConversion
                || Kind == ConversionKind.ExplicitBoxingConversion;
            }
        }

        /// <summary>
        /// Gets a value indicating whether this conversion is user-defined.
        /// </summary>
        /// <value><c>true</c> if this conversion is user-defined; otherwise, <c>false</c>.</value>
        public bool IsUserDefined
        {
            get
            {
                return Kind == ConversionKind.ExplicitUserDefined
                || Kind == ConversionKind.ImplicitUserDefined;
            }
        }

        /// <summary>
        /// Gets a value indicating whether this instance describes an explicit conversion.
        /// </summary>
        /// <value><c>true</c> if the conversion is explicit; otherwise, <c>false</c>.</value>
        public bool IsExplicit
        {
            get
            {
                switch (Kind)
                {
                    case ConversionKind.DynamicCast:
                    case ConversionKind.ExplicitUserDefined:
                    case ConversionKind.ExplicitStaticCast:
                    case ConversionKind.ExplicitBoxingConversion:
                    case ConversionKind.NumberToEnumStaticCast:
                    case ConversionKind.EnumToNumberStaticCast:
                    case ConversionKind.UnboxValueConversion:
                        return true;
                    case ConversionKind.ReinterpretCast:
                    case ConversionKind.ImplicitUserDefined:
                    case ConversionKind.ImplicitStaticCast:
                    case ConversionKind.ImplicitBoxingConversion:
                    case ConversionKind.Identity:
                    case ConversionKind.None:
                    default:
                        return false;
                }
            }
        }

        /// <summary>
        /// A conversion description for a non-existent conversion.
        /// </summary>
        public static readonly ConversionDescription None
            = new SimpleConversionDescription(ConversionKind.None);

        /// <summary>
        /// A conversion description for an identity conversion.
        /// </summary>
        public static readonly ConversionDescription Identity 
            = new SimpleConversionDescription(ConversionKind.Identity);

        /// <summary>
        /// A conversion description for implicit static casts.
        /// </summary>
        public static readonly ConversionDescription ImplicitStaticCast
            = new SimpleConversionDescription(ConversionKind.ImplicitStaticCast);

        /// <summary>
        /// A conversion description for explicit static casts.
        /// </summary>
        public static readonly ConversionDescription ExplicitStaticCast
            = new SimpleConversionDescription(ConversionKind.ImplicitStaticCast);

        /// <summary>
        /// A conversion description for implicit boxing conversions.
        /// </summary>
        public static readonly ConversionDescription ImplicitBoxingConversion
            = new SimpleConversionDescription(ConversionKind.ImplicitBoxingConversion);

        /// <summary>
        /// A conversion description for explicit boxing conversions.
        /// </summary>
        public static readonly ConversionDescription ExplicitBoxingConversion
            = new SimpleConversionDescription(ConversionKind.ExplicitBoxingConversion);

        /// <summary>
        /// A conversion description for unbox-value conversions.
        /// </summary>
        public static readonly ConversionDescription UnboxValueConversion
            = new SimpleConversionDescription(ConversionKind.UnboxValueConversion);

        /// <summary>
        /// A conversion description for enum-to-number static casts.
        /// </summary>
        public static readonly ConversionDescription EnumToNumberStaticCast
            = new SimpleConversionDescription(ConversionKind.EnumToNumberStaticCast);

        /// <summary>
        /// A conversion description for number-to-enum static casts.
        /// </summary>
        public static readonly ConversionDescription NumberToEnumStaticCast
            = new SimpleConversionDescription(ConversionKind.NumberToEnumStaticCast);

        /// <summary>
        /// A conversion description for dynamic casts.
        /// </summary>
        public static readonly ConversionDescription DynamicCast
            = new SimpleConversionDescription(ConversionKind.DynamicCast);

        /// <summary>
        /// A conversion description for reinterpret casts.
        /// </summary>
        public static readonly ConversionDescription ReinterpretCast
            = new SimpleConversionDescription(ConversionKind.ReinterpretCast);

        /// <summary>
        /// Creates a conversion description for an implicit user-defined cast.
        /// </summary>
        /// <param name="ConversionMethod">
        /// The method that implements the user-defined conversion.
        /// </param>
        /// <param name="PreConversion">
        /// The conversion that is used to make input values 
        /// conform to the conversion method's parameter type. 
        /// </param>
        /// <param name="PostConversion">
        /// The conversion that is used to make the conversion
        /// method's return values conform to the conversion's 
        /// target type.
        /// </param>
        /// <returns>
        /// A user-defined conversion description.
        /// </returns>
        public static UserDefinedConversionDescription ImplicitUserDefined(
            IMethod ConversionMethod, 
            ConversionDescription PreConversion, 
            ConversionDescription PostConversion)
        {
            return new UserDefinedConversionDescription(
                ConversionKind.ImplicitUserDefined, 
                ConversionMethod,
                PreConversion,
                PostConversion);
        }

        /// <summary>
        /// Creates a conversion description for an explicit user-defined cast.
        /// </summary>
        /// <param name="ConversionMethod">
        /// The method that implements the user-defined conversion.
        /// </param>
        /// <param name="PreConversion">
        /// The conversion that is used to make input values 
        /// conform to the conversion method's parameter type. 
        /// </param>
        /// <param name="PostConversion">
        /// The conversion that is used to make the conversion
        /// method's return values conform to the conversion's 
        /// target type.
        /// </param>
        /// <returns>
        /// A user-defined conversion description.
        /// </returns>
        public static UserDefinedConversionDescription ExplicitUserDefined(
            IMethod ConversionMethod, 
            ConversionDescription PreConversion, 
            ConversionDescription PostConversion)
        {
            return new UserDefinedConversionDescription(
                ConversionKind.ExplicitUserDefined, 
                ConversionMethod,
                PreConversion,
                PostConversion);
        }

        /// <summary>
        /// A conversion description type for simple conversions, which
        /// do not call methods.
        /// </summary>
        private sealed class SimpleConversionDescription : ConversionDescription
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="Flame.Ecs.Semantics.SimpleConversionDescription"/> class.
            /// </summary>
            /// <param name="Kind">The kind of conversion to perform.</param>
            public SimpleConversionDescription(ConversionKind Kind)
            {
                this.convKind = Kind;
            }

            private ConversionKind convKind;

            /// <summary>
            /// Gets the kind of conversion that is performed.
            /// </summary>
            public override ConversionKind Kind { get { return convKind; } }

            /// <inheritdoc/>
            public override IExpression Convert(IExpression Value, IType TargetType)
            {
                switch (Kind)
                {
                    case ConversionKind.Identity:
                        return Value;
                    case ConversionKind.DynamicCast:
                        return new DynamicCastExpression(Value, TargetType);
                    case ConversionKind.ImplicitStaticCast:
                    case ConversionKind.ExplicitStaticCast:
                        return new StaticCastExpression(Value, TargetType);
                    case ConversionKind.ImplicitBoxingConversion:
                        return new ReinterpretCastExpression(
                            new BoxExpression(Value), TargetType);
                    case ConversionKind.ExplicitBoxingConversion:
                        return new DynamicCastExpression(
                            new BoxExpression(Value), TargetType);
                    case ConversionKind.UnboxValueConversion:
                        return new UnboxValueExpression(Value, TargetType);
                    case ConversionKind.NumberToEnumStaticCast:
                        // An 'enum' static cast requires two casts:
                        // a static cast to the underlying type, 
                        // and a reinterpret cast to or from the 'enum'
                        // type itself.
                        return new ReinterpretCastExpression(
                            new StaticCastExpression(
                                Value, TargetType.GetParent()),
                            TargetType);
                    case ConversionKind.EnumToNumberStaticCast:
                        return new StaticCastExpression(
                            new ReinterpretCastExpression(
                                Value, Value.Type.GetParent()),
                            TargetType);
                    case ConversionKind.ReinterpretCast:
                        return new ReinterpretCastExpression(Value, TargetType);
                    case ConversionKind.None:
                    default:
                        throw new InvalidOperationException();
                }
            }

            public override string ToString()
            {
                return string.Format("[SimpleConversionDescription: Kind={0}]", Kind);
            }
        }
    }

    /// <summary>
    /// A conversion description for user-defined conversions,
    /// that is, conversions that rely on method invocations.
    /// </summary>
    public sealed class UserDefinedConversionDescription : ConversionDescription
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Flame.Ecs.Semantics.UserDefinedConversionDescription"/> class.
        /// </summary>
        /// <param name="Kind">
        /// The kind of conversion to perform. This can either be 
        /// an explicit user-defined conversion or an implicit 
        /// user-defined conversion.
        /// </param>
        /// <param name="ConversionMethod">
        /// The method that implements the user-defined conversion.
        /// </param>
        internal UserDefinedConversionDescription(
            ConversionKind Kind, IMethod ConversionMethod,
            ConversionDescription PreConversion,
            ConversionDescription PostConversion)
        {
            this.convKind = Kind;
            this.ConversionMethod = ConversionMethod;
            this.PreConversion = PreConversion;
            this.PostConversion = PostConversion;
        }

        private ConversionKind convKind;

        /// <summary>
        /// Gets the kind of conversion that is performed.
        /// </summary>
        public override ConversionKind Kind { get { return convKind; } }

        /// <summary>
        /// Gets the method that is used to perform the user-defined
        /// conversion.
        /// </summary>
        public IMethod ConversionMethod { get; private set; }

        /// <summary>
        /// Gets the conversion that is used to make input values 
        /// conform to the conversion method's parameter type. 
        /// </summary>
        /// <value>The pre-conversion.</value>
        public ConversionDescription PreConversion { get; private set; }

        /// <summary>
        /// Gets the conversion that is used to make the conversion
        /// method's return values conform to the conversion's 
        /// target type.
        /// </summary>
        /// <value>The post-conversion.</value>
        public ConversionDescription PostConversion { get; private set; }

        /// <summary>
        /// Gets the type of the conversion method parameter,
        /// or the declaring type, whichever is applicable.
        /// </summary>
        /// <value>
        /// The type of the conversion method parameter,
        /// or the declaring type, whichever is applicable.
        /// </value>
        public static IType GetConversionMethodParameterType(IMethod Method)
        {
            if (Method.IsStatic)
                return Method.Parameters.Single().ParameterType;
            else
                return Method.DeclaringType;
        }

        /// <summary>
        /// Creates an expression that invokes the user-defined
        /// conversion method with the given expression as 
        /// argument.
        /// </summary>
        /// <returns>The invocation-expression.</returns>
        /// <param name="Value">The argument to the invocation.</param>
        private IExpression CreateCall(IExpression Value)
        {
            if (ConversionMethod.IsStatic)
                return new InvocationExpression(
                    ConversionMethod, null, 
                    new IExpression[] { Value });
            else
                return new InvocationExpression(
                    ConversionMethod, 
                    ExpressionConverters.AsTargetExpression(Value), 
                    null);
        }

        /// <inheritdoc/>
        public override IExpression Convert(
            IExpression Value, IType TargetType)
        {
            return PostConversion.Convert(
                CreateCall(
                    PreConversion.Convert(
                        Value, 
                        GetConversionMethodParameterType(
                            ConversionMethod))), 
                TargetType);
        }

        public override string ToString()
        {
            return string.Format(
                "[UserDefinedConversionDescription: Kind={0}, ConversionMethod={1}, PreConversion={2}, PostConversion={3}]", 
                Kind, ConversionMethod, PreConversion, PostConversion);
        }
    }
}

