using Loyc;
using Loyc.Syntax;
using Flame;
using Flame.Compiler;
using System;
using System.Collections.Generic;
using System.Linq;
using Pixie;
using Flame.Build;

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
			{
				var left = ToQualifiedName(Node.Args[0]);
				var right = ToQualifiedName(Node.Args[1]);
				return left == null || right == null ? null : right.Qualify(left);
			}
			else if (Node.IsId)
				return new QualifiedName(name.Name);
			else
				return null;
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
				return false;
			}
			else
			{
				return true;
			}
		}

		public static IGenericParameter ToGenericParameter(
			LNode Node, IGenericMember Parent, GlobalScope Scope)
		{
			if (!Node.IsId)
			{
				Scope.Log.LogError(new LogEntry(
					"invalid syntax",
					"generic parameters must defined by a simple identifier.",
					ToSourceLocation(Node.Range)));
				return null;
			}

			var name = Node.Name.Name;
			return new DescribedGenericParameter(name, Parent);
		}

		public static Tuple<string, Func<IGenericMember, IEnumerable<IGenericParameter>>> ToUnqualifiedName(
			LNode Node, GlobalScope Scope)
		{
			var name = Node.Name;
			if (name == CodeSymbols.Of)
			{
				var innerResults = ToUnqualifiedName(Node.Args[0], Scope);

				return Tuple.Create<string, Func<IGenericMember, IEnumerable<IGenericParameter>>>(
					innerResults.Item1, parent =>
					{
						var genParams = new List<IGenericParameter>(innerResults.Item2(parent));
						foreach (var item in Node.Args.Slice(1))
						{
							var genParam = ToGenericParameter(item, parent, Scope);
							if (genParam != null)
								genParams.Add(genParam);
						}
						return genParams;
					});
			}
			else
			{
				return Tuple.Create<string, Func<IGenericMember, IEnumerable<IGenericParameter>>>(
					name.Name, _ => Enumerable.Empty<IGenericParameter>());
			}
		}
	}
}

