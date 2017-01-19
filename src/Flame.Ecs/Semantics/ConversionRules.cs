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
        /// Classifies a conversion of the given expression to the given type,
        /// within the context of the given function scope.
        /// </summary>
        public virtual IReadOnlyList<ConversionDescription> ClassifyConversion(
            IExpression Source, IType Type, FunctionScope Scope)
        {
            return ClassifyConversion(Source.Type, Type, Scope);
        }

        /// <summary>
        /// Classifies a conversion of the given source type to the given target type,
        /// within the context of the given function scope.
        /// </summary>
        public abstract IReadOnlyList<ConversionDescription> ClassifyConversion(
            IType SourceType, IType TargetType, FunctionScope Scope);
    }
}

