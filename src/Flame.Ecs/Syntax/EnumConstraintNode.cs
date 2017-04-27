using System;

namespace Flame.Ecs.Syntax
{
    /// <summary>
    /// A generic 'enum' constraint.
    /// </summary>
    public sealed class EnumConstraintNode : IGenericConstraintNode
    {
        private EnumConstraintNode()
        { }

        public static readonly EnumConstraintNode Instance = new EnumConstraintNode();

        /// <inheritdoc/>
        public IGenericConstraint Analyze(LocalScope Scope, NodeConverter Converter)
        {
            return EnumConstraint.Instance;
        }
    }
}

