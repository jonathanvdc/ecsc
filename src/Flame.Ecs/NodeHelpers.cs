using Loyc;
using Loyc.Syntax;
using Flame;
using Flame.Compiler;
using System;
using System.Collections.Generic;

namespace Flame.Ecs
{
	public static class NodeHelpers
	{
		/// <summary>
		/// Converts the given Loyc `SourceRange` to a Flame `SourceLocation`.
		/// </summary>
		public static SourceLocation ToSourceLocation(SourceRange Range)
		{
			var doc = new LoycSourceDocument(Range.Source);
			return new SourceLocation(doc, Range.StartIndex, Range.Length);
		}
	}
}

