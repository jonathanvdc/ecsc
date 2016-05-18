using System;
using Flame.Compiler;

namespace Flame.Ecs
{
	/// <summary>
	/// Lists a number of EC#-specific warnings.
	/// </summary>
	public static class EcsWarnings
	{
		/// <summary>
		/// The -Wduplicate-access-modifier warning.
		/// </summary>
		/// <remarks>
		/// This is a -Wall warning.
		/// </remarks>
		public static readonly WarningDescription DuplicateAccessModifierWarning = 
			new WarningDescription("duplicate-access-modifier", Warnings.Instance.All);

        /// <summary>
        /// The -Winvalid-main-sig warning.
        /// </summary>
        /// <remarks>
        /// This is a -Wall warning.
        /// </remarks>
        public static readonly WarningDescription InvalidMainSignatureWarning = 
            new WarningDescription("invalid-main-sig", Warnings.Instance.All);

        /// <summary>
        /// The -Wgeneric-main-sig warning.
        /// </summary>
        /// <remarks>
        /// This is a -Winvalid-main-sig warning.
        /// </remarks>
        public static readonly WarningDescription GenericMainSignatureWarning =
            new WarningDescription("generic-main-sig", InvalidMainSignatureWarning);

        /// <summary>
        /// The -Walways warning group, which is responsible for
        /// flagging expressions that always evaluate to some 
        /// known value.
        /// </summary>
        /// <remarks>
        /// This is a -Wall warning group.
        /// </remarks>
        public static readonly WarningDescription AlwaysWarningGroup = 
            new WarningDescription("always", Warnings.Instance.All);

        /// <summary>
        /// The -Walways-null warning.
        /// </summary>
        /// <remarks>
        /// This is a -Wall warning.
        /// </remarks>
        public static readonly WarningDescription AlwaysNullWarning = 
            new WarningDescription("always-null", AlwaysWarningGroup);

        /// <summary>
        /// The -Walways-true warning.
        /// </summary>
        /// <remarks>
        /// This is a -Wall warning.
        /// </remarks>
        public static readonly WarningDescription AlwaysTrueWarning = 
            new WarningDescription("always-true", AlwaysWarningGroup);

        /// <summary>
        /// The -Walways-false warning.
        /// </summary>
        /// <remarks>
        /// This is a -Wall warning.
        /// </remarks>
        public static readonly WarningDescription AlwaysFalseWarning = 
            new WarningDescription("always-false", AlwaysWarningGroup);

        /// <summary>
        /// The -Wredundant-as warning.
        /// </summary>
        /// <remarks>
        /// This is a -Wall warning.
        /// </remarks>
        public static readonly WarningDescription RedundantAsWarning = 
            new WarningDescription("redundant-as", Warnings.Instance.All);

        /// <summary>
        /// The -Wecs warning group.
        /// </summary>
        /// <remarks>
        /// This is a -pedantic warning group.
        /// </remarks>
        public static readonly WarningDescription EcsExtensionWarningGroup = 
            new WarningDescription("ecs", Warnings.Instance.Pedantic);

        /// <summary>
        /// The -Wecs-using-cast warning.
        /// </summary>
        /// <remarks>
        /// This is a -Wecs warning.
        /// </remarks>
        public static readonly WarningDescription EcsExtensionUsingCastWarning = 
            new WarningDescription("ecs-using-cast", EcsExtensionWarningGroup);
	}
}

