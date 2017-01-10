using System;
using Loyc.Syntax;

namespace Flame.Ecs.Syntax
{
    /// <summary>
    /// A generic constraint node which makes sure that a generic parameter
    /// inherits from or implements a specific type. 
    /// </summary>
    public sealed class TypeConstraintNode : IGenericConstraintNode
    {
        public TypeConstraintNode(LNode TypeNode)
        {
            this.TypeNode = TypeNode;
        }

        /// <summary>
        /// Gets the syntax node that describes the type
        /// to which a generic parameter is to be constrained.
        /// </summary>
        /// <value>The type node.</value>
        public LNode TypeNode { get; private set; }

        /// <inheritdoc/>
        public IGenericConstraint Analyze(GlobalScope Scope, NodeConverter Converter)
        {
            var type = Converter.ConvertCheckedType(TypeNode, Scope);
            return type == null
                ? null
                : new TypeConstraint(type);
        }
    }
}

