using System;
using System.Collections.Generic;
using Flame.Compiler;
using Flame.Compiler.Expressions;

namespace Flame.Ecs.Semantics
{
    /// <summary>
    /// An interface that specifies a system of explicit and implicit
    /// conversion rules.
    /// </summary>
    public abstract class ConversionRules
    {
        /// <summary>
        /// Classifies a conversion of the given expression to the given type.
        /// </summary>
        public virtual IReadOnlyList<ConversionDescription> ClassifyConversion(
            IExpression Source, IType Type)
        {
            return ClassifyConversion(Source.Type, Type);
        }

        /// <summary>
        /// Classifies a conversion of the given source type to the given target type.
        /// </summary>
        public abstract IReadOnlyList<ConversionDescription> ClassifyConversion(
            IType SourceType, IType TargetType);

        /// <summary>
        /// Finds out whether a value of the given source type
        /// can be converted implicitly to the given target type.
        /// </summary>
        public bool HasImplicitConversion(IType From, IType To)
        {
            foreach (var conv in ClassifyConversion(From, To))
            {
                if (conv.IsImplicit)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Finds out whether the given value
        /// can be converted implicitly to the given target type.
        /// </summary>
        public bool HasImplicitConversion(IExpression From, IType To)
        {
            foreach (var conv in ClassifyConversion(From, To))
            {
                if (conv.IsImplicit)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Finds out whether a value of the given source type
        /// can be converted to the given target type by using
        /// a reference conversion.
        /// </summary>
        public bool HasReferenceConversion(IType From, IType To)
        {
            foreach (var conv in ClassifyConversion(From, To))
            {
                if (conv.Kind == ConversionKind.DynamicCast)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Finds out whether the given value
        /// can be converted to the given target type by using
        /// a reference conversion.
        /// </summary>
        public bool HasReferenceConversion(IExpression From, IType To)
        {
            foreach (var conv in ClassifyConversion(From, To))
            {
                if (conv.Kind == ConversionKind.DynamicCast)
                    return true;
            }
            return false;
        }
    }
}

