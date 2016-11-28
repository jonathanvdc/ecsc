using System;
using System.Collections.Generic;

namespace Flame.Ecs.Syntax
{
    /// <summary>
    /// A data structure that represents a generic parameter
    /// definition.
    /// </summary>
    public struct GenericParameterDef
    {
        public GenericParameterDef(
            SimpleName Name, 
            IReadOnlyList<IGenericConstraintNode> Constraints)
        {
            this.Name = Name;
            this.Constraints = Constraints;
        }

        /// <summary>
        /// Gets the defined generic parameter's name.
        /// </summary>
        /// <value>The name of the generic parameter that is defined.</value>
        public SimpleName Name { get; private set; }

        /// <summary>
        /// Gets the defined generic parameter's list of
        /// generic constraints nodes.
        /// </summary>
        /// <value>The generic parameter's constraints.</value>
        public IReadOnlyList<IGenericConstraintNode> Constraints { get; private set; }
    }
}

