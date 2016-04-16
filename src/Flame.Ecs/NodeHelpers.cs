using Loyc;
using Loyc.Syntax;
using Flame;
using Flame.Compiler;
using System;
using System.Collections.Generic;
using Pixie;

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

		/// <summary>
		/// Converts the given node to a qualified name.
		/// </summary>
		public static QualifiedName ToQualifiedName(LNode Node)
		{
			var name = Node.Name;
			if (Node.IsCall && (name == CodeSymbols.Dot || name == CodeSymbols.ColonColon))
				return ToQualifiedName(Node.Args[1]).Qualify(ToQualifiedName(Node.Args[0]));
			else
				return new QualifiedName(name.Name);
		}

		/// <summary>
		/// Produces an array of markup nodes, where the 
		/// even arguments are highlighted.
		/// </summary>
		public static MarkupNode[] HighlightEven(params string[] Text)
		{
			var results = new MarkupNode[Text.Length];
			for (int i = 0; i < Text.Length; i++)
			{
				results[i] = new MarkupNode(
					i % 2 == 0 ? NodeConstants.TextNodeType : NodeConstants.BrightNodeType,
					Text[i]);
			}
			return results;
		}

		/// <summary>
		/// Checks the given node's arity.
		/// </summary>
		public static bool CheckArity(LNode Node, int Arity, ICompilerLog Log)
		{
			if (Node.ArgCount != Arity)
			{
				Log.LogError(new LogEntry(
					"unexpected node arity", 
					HighlightEven(
						"syntax node '", Node.Name.Name, "' had an argument count of '", 
						Node.ArgCount.ToString(), "'. Expected: '", Arity.ToString(), "'."),
					ToSourceLocation(Node.Range)));
				return true;
			}
			else
			{
				return false;
			}
		}
	}
}

