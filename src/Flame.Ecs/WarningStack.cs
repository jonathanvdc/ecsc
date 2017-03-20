using System.Collections.Generic;

namespace Flame.Ecs
{
    /// <summary>
    /// A stack of warnings which are either explicitly disabled or restored.
    /// </summary>
    public sealed class WarningStack
    {
        /// <summary>
        /// Creates an empty warning stack.
        /// </summary>
        public WarningStack()
        {
            this.warningStates = new Dictionary<string, bool>();
        }

        /// <summary>
        /// Creates a warning stack with the given parent entry and warning state dictionary.
        /// </summary>
        /// <param name="warningStates">The warning states for this warning stack.</param>
        private WarningStack(Dictionary<string, bool> warningStates)
        {
            this.warningStates = new Dictionary<string, bool>();
        }

        /// <summary>
        /// Specifies which warnings have been disabled and which have been restored.
        /// </summary>
        private Dictionary<string, bool> warningStates;

        private WarningStack Push(bool AreEnabled, params string[] WarningNames)
        {
            var newStates = new Dictionary<string, bool>(warningStates);
            foreach (var name in WarningNames)
                newStates[name] = AreEnabled;

            return new WarningStack(newStates);
        }

        /// <summary>
        /// Creates a warning stack that copies all warning states from this stack and restores
        /// the given warnings.
        /// </summary>
        /// <param name="WarningNames">The warnings to restore.</param>
        /// <returns>The new warning stack.</returns>
        public WarningStack PushRstore(params string[] WarningNames)
        {
            return Push(true, WarningNames);
        }

        /// <summary>
        /// Creates a warning stack that copies all warning states from this stack and disables
        /// the given warnings.
        /// </summary>
        /// <param name="WarningNames">The warnings to disable.</param>
        /// <returns>The new warning stack.</returns>
        public WarningStack PushDisable(params string[] WarningNames)
        {
            return Push(false, WarningNames);
        }

        /// <summary>
        /// Tells if the warning with the given name is disabled by this warning stack.
        /// </summary>
        /// <param name="WarningName">The name of the warning.</param>
        /// <returns>
        /// A Boolean flag that tells if the warning with the given name is disabled by
        /// this warning stack.
        /// </returns>
        public bool IsDisabled(string WarningName)
        {
            bool result;
            if (warningStates.TryGetValue(WarningName, out result))
                return !result;
            else
                return false;
        }
    }
}