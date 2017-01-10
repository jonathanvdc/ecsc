using System;
using System.Collections.Generic;

namespace Flame.Ecs.Syntax
{
    /// <summary>
    /// A data structure that stores a generic member definition's
    /// name.
    /// </summary>
    public struct GenericMemberName
    {
        public GenericMemberName(
            SimpleName Name, 
            IReadOnlyList<GenericParameterDef> GenericParameters)
        {
            this.Name = Name;
            this.GenericParameters = GenericParameters;
        }

        /// <summary>
        /// Gets the generic member's name.
        /// </summary>
        /// <value>The name of the generic member itself.</value>
        public SimpleName Name { get; private set; }

        /// <summary>
        /// Gets the generic parameter definitions.
        /// </summary>
        /// <value>The generic parameter definitions.</value>
        public IReadOnlyList<GenericParameterDef> GenericParameters { get; private set; }
    }
}

