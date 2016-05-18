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
                return left.IsEmpty || right.IsEmpty 
                    ? default(QualifiedName) 
                    : right.Qualify(left);
			}
			else if (Node.IsId)
				return new QualifiedName(name.Name);
			else
                return default(QualifiedName);
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

		/// <summary>
		/// Checks the given node's minimal arity.
		/// </summary>
		public static bool CheckMinArity(LNode Node, int MinArity, ICompilerLog Log)
		{
			if (Node.ArgCount < MinArity)
			{
				Log.LogError(new LogEntry(
					"unexpected node arity", 
					HighlightEven(
						"syntax node '", Node.Name.Name, "' had an argument count of '", 
						Node.ArgCount.ToString(), "'. Expected: at least '", MinArity.ToString(), "'."),
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

		public static Tuple<SimpleName, Func<IGenericMember, IEnumerable<IGenericParameter>>> ToUnqualifiedName(
			LNode Node, GlobalScope Scope)
		{
			var name = Node.Name;
            if (name == CodeSymbols.Of)
            {
                var innerResults = ToUnqualifiedName(Node.Args[0], Scope);

                return Tuple.Create<SimpleName, Func<IGenericMember, IEnumerable<IGenericParameter>>>(
                    new SimpleName(
                        innerResults.Item1.Name, 
                        innerResults.Item1.TypeParameterCount + Node.Args.Count - 1), 
                    parent =>
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
            else if (!Node.IsId)
            {
                Scope.Log.LogError(new LogEntry(
                    "invalid syntax",
                    NodeHelpers.HighlightEven(
                        "node '", Node.ToString(), 
                        "' could not be interpreted as an unqualified name."),
                    NodeHelpers.ToSourceLocation(Node.Range)));
            }
            return Tuple.Create<SimpleName, Func<IGenericMember, IEnumerable<IGenericParameter>>>(
                new SimpleName(name.Name), _ => Enumerable.Empty<IGenericParameter>());
		}

        /// <summary>
        /// Checks that the given node is a valid
        /// identifier node, i.e. it describes a valid
        /// name.
        /// </summary>
        public static bool CheckValidIdentifier(LNode IdNode, ICompilerLog Log)
        {
            if (!IdNode.IsId)
            {
                Log.LogError(new LogEntry(
                    "invalid syntax",
                    NodeHelpers.HighlightEven("expected an identifier."),
                    NodeHelpers.ToSourceLocation(IdNode.Range)));
                return false;
            }
            else if (IdNode.HasSpecialName)
            {
                Log.LogError(new LogEntry(
                    "invalid syntax",
                    NodeHelpers.HighlightEven("'", IdNode.Name.Name, "' is not an acceptable identifier."),
                    NodeHelpers.ToSourceLocation(IdNode.Range)));
                return false;
            }
            else
            {
                return true;
            }
        }

        /// <summary>
        /// If the given node is a valid identifier, then an (identifier, null)
        /// tuple is returned. If it is an assignment to an identifier, then
        /// that assignment will be decomposed, and an (identifier, value)
        /// tuple is returned.
        /// </summary>
        public static Tuple<LNode, LNode> DecomposeAssignOrId(LNode AssignOrId, ICompilerLog Log)
        {
            if (AssignOrId.Calls(CodeSymbols.Assign))
            {
                if (!NodeHelpers.CheckArity(AssignOrId, 2, Log) 
                    || !CheckValidIdentifier(AssignOrId.Args[0], Log))
                    return null;
                else
                    return Tuple.Create(AssignOrId.Args[0], AssignOrId.Args[1]);
            }
            else if (AssignOrId.IsId)
            {
                if (!CheckValidIdentifier(AssignOrId, Log))
                    return null;
                else
                    return Tuple.Create<LNode, LNode>(AssignOrId, null);
            }
            else
            {
                Log.LogError(new LogEntry(
                    "invalid syntax",
                    "expected an identifier or an assignment to an identifier.",
                    NodeHelpers.ToSourceLocation(AssignOrId.Range)));
                return null;
            }
        }

		/// <summary>
		/// Partition the specified sequence based on 
		/// the given predicate.
		/// </summary>
		public static Tuple<IEnumerable<T>, IEnumerable<T>> Partition<T>(
			IEnumerable<T> Sequence, Func<T, bool> Predicate)
		{
			var first = new List<T>();
			var second = new List<T>();
			foreach (var item in Sequence)
			{
				if (Predicate(item))
					first.Add(item);
				else
					second.Add(item);
			}
			return Tuple.Create<IEnumerable<T>, IEnumerable<T>>(first, second);
		}

		/// <summary>
		/// Determines if the given symbol is an access modifier attribute.
		/// </summary>
		public static bool IsAccessModifier(Symbol S)
		{
			return accessModifiers.Contains(S);
		}

		/// <summary>
		/// Tries to find an access modifier that matches the given
		/// set of access modifier symbols.
		/// </summary>
		public static AccessModifier? ToAccessModifier(HashSet<Symbol> Symbols)
		{
			AccessModifier result;
			if (accModMap.TryGetValue(Symbols, out result))
				return result;
			else
				return null;
		}

		private static readonly HashSet<Symbol> accessModifiers = new HashSet<Symbol>()
		{
			CodeSymbols.Private, CodeSymbols.Protected,
			CodeSymbols.Internal, CodeSymbols.Public,
			CodeSymbols.ProtectedIn, CodeSymbols.ProtectedIn,
			CodeSymbols.FilePrivate
		};

		private static readonly IReadOnlyDictionary<HashSet<Symbol>, AccessModifier> accModMap = new Dictionary<HashSet<Symbol>, AccessModifier>(HashSet<Symbol>.CreateSetComparer())
		{
			{ new HashSet<Symbol>() { CodeSymbols.Private }, AccessModifier.Private },
			{ new HashSet<Symbol>() { CodeSymbols.Protected }, AccessModifier.Protected },
			{ new HashSet<Symbol>() { CodeSymbols.Internal }, AccessModifier.Assembly },
			{ new HashSet<Symbol>() { CodeSymbols.Public }, AccessModifier.Public },
			{ new HashSet<Symbol>() { CodeSymbols.Protected, CodeSymbols.Internal }, AccessModifier.ProtectedOrAssembly },
		};
	}
}

