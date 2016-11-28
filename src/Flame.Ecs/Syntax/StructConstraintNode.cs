using System;

namespace Flame.Ecs.Syntax
{
    /// <summary>
    /// A generic 'struct' constraint.
    /// </summary>
    public sealed class StructConstraintNode : IGenericConstraintNode
    {
        private StructConstraintNode()
        { }

        public static readonly StructConstraintNode Instance = new StructConstraintNode();

        /// <inheritdoc/>
        public IGenericConstraint Analyze(GlobalScope Scope, NodeConverter Converter)
        {
            return ValueTypeConstraint.Instance;
        }
    }
}

