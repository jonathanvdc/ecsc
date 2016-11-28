using System;

namespace Flame.Ecs.Syntax
{
    /// <summary>
    /// A common interface for data structures that specify
    /// generic constraints. 
    /// </summary>
    public interface IGenericConstraintNode
    {
        /// <summary>
        /// Analyze the this generic constraint within 
        /// the context of the given scope and node converter.
        /// </summary>
        /// <param name="Scope">
        /// The scope in which the generic constraint is analyzed.
        /// </param>
        /// <param name="Converter">
        /// The converter which is used to analyze child nodes of the generic constraint.
        /// </param>
        IGenericConstraint Analyze(GlobalScope Scope, NodeConverter Converter);
    }
}

