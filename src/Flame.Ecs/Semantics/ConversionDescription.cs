using System;
using Flame.Compiler;
using Flame.Compiler.Expressions;

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
    public struct ConversionDescription
    {
        public ConversionDescription(ConversionKind Kind)
            : this(Kind, null)
        {
        }
        public ConversionDescription(ConversionKind Kind, IMethod ConversionMethod)
        {
            this.Kind = Kind;
            this.ConversionMethod = ConversionMethod;
        }

        /// <summary>
        /// Gets the kind of conversion that is performed.
        /// </summary>
        public ConversionKind Kind { get; private set; }

        /// <summary>
        /// Gets the method that is used to perform a user-defined
        /// conversion, if any.
        /// </summary>
        public IMethod ConversionMethod { get; private set; }

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
        /// <value><c>true</c> if this instance is static; otherwise, <c>false</c>.</value>
        public bool IsStatic
        {
            get { return Kind != ConversionKind.DynamicCast; }
        }

        /// <summary>
        /// Gets a value indicating whether this instance describes a reference conversion.
        /// </summary>
        /// <value><c>true</c> if this instance is reference; otherwise, <c>false</c>.</value>
        public bool IsReference
        {
            get 
            { 
                return Kind == ConversionKind.DynamicCast 
                    || Kind == ConversionKind.ReinterpretCast; 
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
                    case ConversionKind.NumberToEnumStaticCast:
                    case ConversionKind.EnumToNumberStaticCast:
                        return true;
                    case ConversionKind.ReinterpretCast:
                    case ConversionKind.ImplicitUserDefined:
                    case ConversionKind.ImplicitStaticCast:
                    case ConversionKind.Identity:
                    case ConversionKind.None:
                    default:
                        return false;
                }
            }
        }

        /// <summary>
        /// Uses the information captured by this conversion
        /// description to convert the given expression to the
        /// given type.
        /// </summary>
        public IExpression Convert(IExpression Value, IType TargetType)
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
                case ConversionKind.ImplicitUserDefined:
                case ConversionKind.ExplicitUserDefined:
                    if (ConversionMethod.IsStatic)
                        return new InvocationExpression(
                            ConversionMethod, null, 
                            new IExpression[] { Value });
                    else
                        return new InvocationExpression(
                            ConversionMethod, 
                            ExpressionConverters.AsTargetObject(Value), 
                            null);
                case ConversionKind.None:
                default:
                    throw new InvalidOperationException();
            }
        }
    }
}

