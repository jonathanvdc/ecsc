using System;
using Flame.Compiler;
using Flame.Compiler.Expressions;

namespace Flame.Ecs
{
	/// <summary>
	/// An interface that specifies a system of explicit and implicit
	/// conversion rules.
	/// </summary>
	public interface IConversionRules
	{
		/// <summary>
		/// Finds out whether a value of the given source type
		/// can be converted implicitly to the given target type.
		/// </summary>
		bool HasImplicitConversion(IType From, IType To);

		/// <summary>
		/// Implicitly converts the given expression to the given type.
		/// Null is returned if this is not legal.
		/// </summary>
		IExpression TryConvertImplicit(IExpression From, IType To);

        /// <summary>
        /// Explicitly the given expression to the given type, but only
        /// if this conversion is guaranteed to succeed.
        /// Null is returned if the given conversion is not possible
        /// under the aforementioned constraints.
        /// </summary>
        IExpression TryConvertStatic(IExpression From, IType To);

		/// <summary>
		/// Explicitly converts the given expression to the given type.
		/// Null is returned if this is not legal.
		/// </summary>
		IExpression TryConvertExplicit(IExpression From, IType To);
	}

	/// <summary>
	/// Conversion rules for the EC# programming language.
	/// </summary>
	public sealed class EcsConversionRules : IConversionRules
	{
		private EcsConversionRules() { }

		public static readonly EcsConversionRules Instance = new EcsConversionRules(); 

		/// <summary>
		/// Finds out whether a value of the given source type
		/// can be converted implicitly to the given target type.
		/// </summary>
		public bool HasImplicitConversion(IType From, IType To)
		{
			// TODO: take primitive and pointer conversions into
			// account here.
			return From.Is(To);
		}

		/// <summary>
		/// Implicitly converts the given expression to the given type.
		/// </summary>
		public IExpression TryConvertImplicit(IExpression From, IType To)
		{
			if (!HasImplicitConversion(From.Type, To))
				return null;

			return ConversionExpression.Instance.Create(From, To);
		}

        /// <summary>
        /// Explicitly the given expression to the given type, but only
        /// if this conversion is guaranteed to succeed.
        /// Null is returned if the given conversion is not possible
        /// under the aforementioned constraints.
        /// </summary>
        public IExpression TryConvertStatic(IExpression From, IType To)
        {
            var result = ConversionExpression.Instance.Create(From, To);

            // HACK: just let ConversionExpression handle this,
            // and check for dynamic casts.
            // TODO: implement this properly.
            if (result is DynamicCastExpression)
                return null;
            else
                return result;
        }

		/// <summary>
		/// Explicitly converts the given expression to the given type.
		/// </summary>
		public IExpression TryConvertExplicit(IExpression From, IType To)
		{
			return ConversionExpression.Instance.Create(From, To);
		}
	}
}

