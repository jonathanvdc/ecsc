using System;

namespace Flame.Ecs.Syntax
{
    /// <summary>
    /// A generic 'class' constraint.
    /// </summary>
    public sealed class ClassConstraintNode : IGenericConstraintNode
    {
        private ClassConstraintNode()
        { }

        public static readonly ClassConstraintNode Instance = new ClassConstraintNode();

        /// <inheritdoc/>
        public IGenericConstraint Analyze(GlobalScope Scope, NodeConverter Converter)
        {
            return ReferenceTypeConstraint.Instance;
        }
    }
}

