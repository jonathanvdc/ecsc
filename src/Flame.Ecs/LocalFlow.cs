using Flame.Compiler;

namespace Flame.Ecs
{
    /// <summary>
    /// Captures information related to control-flow in a local scope.
    /// </summary>
    public struct LocalFlow
    {
        public LocalFlow(UniqueTag BreakTag, UniqueTag ContinueTag)
        {
            this.BreakTag = BreakTag;
            this.ContinueTag = ContinueTag;
        }

        /// <summary>
        /// Gets the enclosing control-flow node's tag for 'continue'
        /// statements. Returns null if there is no such tag.
        /// </summary>
        /// <remarks>
        /// This tag can be used as a target for 'continue' statements.
        /// </remarks>
        public UniqueTag BreakTag { get; private set; }

        /// <summary>
        /// Gets the enclosing control-flow node's tag for 'break'
        /// statements. Returns null if there is no such tag.
        /// </summary>
        /// <remarks>
        /// This tag can be used as a target for 'break' statements.
        /// </remarks>
        public UniqueTag ContinueTag { get; private set; }
    }
}