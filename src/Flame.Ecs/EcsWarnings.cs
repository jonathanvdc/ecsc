﻿using System;
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
	}
}

