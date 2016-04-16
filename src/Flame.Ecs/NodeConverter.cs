using System;
using System.Collections.Generic;
using System.Linq;
using Loyc.Syntax;
using Loyc;
using Flame.Build;
using Flame.Compiler;
using Flame.Compiler.Expressions;
using Pixie;

namespace Flame.Ecs
{
	using GlobalConverter = Func<LNode, IMutableNamespace, GlobalScope, NodeConverter, GlobalScope>;
	using TypeConverter = Func<LNode, GlobalScope, NodeConverter, IType>;
	using TypeMemberConverter = Func<LNode, DescribedType, GlobalScope, NodeConverter, GlobalScope>;
	using AttributeConverter = Func<LNode, GlobalScope, NodeConverter, IAttribute>;
	using ExpressionConverter = Func<LNode, ILocalScope, NodeConverter, IExpression>;

	/// <summary>
	/// Defines a type that semantically analyzes a syntax tree by
	/// applying sub-converters to syntax nodes.
	/// </summary>
	public sealed class NodeConverter
	{
		public NodeConverter(
			Func<string, ILocalScope, IExpression> LookupUnqualifiedName)
		{
			this.LookupUnqualifiedName = LookupUnqualifiedName;
			this.globalConverters = new Dictionary<Symbol, GlobalConverter>();
			this.typeConverters = new Dictionary<Symbol, TypeConverter>();
			this.typeMemberConverters = new Dictionary<Symbol, TypeMemberConverter>();
			this.attrConverters = new Dictionary<Symbol, AttributeConverter>();
			this.exprConverters = new Dictionary<Symbol, ExpressionConverter>();
		}

		private Dictionary<Symbol, GlobalConverter> globalConverters;
		private Dictionary<Symbol, TypeConverter> typeConverters;
		private Dictionary<Symbol, TypeMemberConverter> typeMemberConverters;
		private Dictionary<Symbol, AttributeConverter> attrConverters;
		private Dictionary<Symbol, ExpressionConverter> exprConverters;

		public Func<string, ILocalScope, IExpression> LookupUnqualifiedName { get; private set; }

		/// <summary>
		/// Tries to get the appropriate converter for the given
		/// node. If that can't be done, then null is returned,
		/// </summary>
		private static T GetConverterOrDefault<T>(
			Dictionary<Symbol, T> Converters, LNode Node)
		{
			var name = Node.Name;
			T result;
			if (name == GSymbol.Empty || !Converters.TryGetValue(name, out result))
				return default(T);
			else
				return result;
		}

		/// <summary>
		/// Logs an error message that states that a node could not
		/// be converted.
		/// </summary>
		private static void LogCannotConvert(LNode Node, ICompilerLog Log)
		{
			Log.LogError(new LogEntry(
				"unknown node",
				NodeHelpers.HighlightEven(
					"syntax node '", Node.Name.Name, 
					"' could not be converted because its node type was not recognized " +
					"as a known node type. (in this context)"),
				NodeHelpers.ToSourceLocation(Node.Range)));
		}

		/// <summary>
		/// Converts a global node.
		/// </summary>
		public GlobalScope ConvertGlobal(
			LNode Node, IMutableNamespace Namespace, GlobalScope Scope)
		{
			var conv = GetConverterOrDefault(globalConverters, Node);
			if (conv == null)
			{
				LogCannotConvert(Node, Scope.Log);
				return Scope;
			}
			else
			{
				return conv(Node, Namespace, Scope, this);
			}
		}

		/// <summary>
		/// Converts a type member node. A tuple is returned
		/// that represents a potential
		/// </summary>
		public GlobalScope ConvertTypeMember(
			LNode Node, DescribedType DeclaringType, GlobalScope Scope)
		{
			var conv = GetConverterOrDefault(typeMemberConverters, Node);
			if (conv == null)
			{
				return ConvertGlobal(Node, new TypeNamespace(DeclaringType), Scope);
			}
			else
			{
				return conv(Node, DeclaringType, Scope, this);
			}
		}

		/// <summary>
		/// Converts the given type reference node.
		/// </summary>
		public IType ConvertType(
			LNode Node, GlobalScope Scope)
		{
			var conv = GetConverterOrDefault(typeConverters, Node);
			if (conv == null)
			{
				var qualName = NodeHelpers.ToQualifiedName(Node);
				if (qualName == null)
				{
					LogCannotConvert(Node, Scope.Log);
					return null;
				}
				else
				{
					return Scope.Binder.BindType(qualName);
				}
			}
			else
			{
				return conv(Node, Scope, this);
			}
		}

		/// <summary>
		/// Converts an attribute node. Null is returned if that fails.
		/// </summary>
		public IAttribute ConvertAttribute(
			LNode Node, GlobalScope Scope)
		{
			var conv = GetConverterOrDefault(attrConverters, Node);
			if (conv == null)
			{
				LogCannotConvert(Node, Scope.Log);
				return null;
			}
			else
			{
				return conv(Node, Scope, this);
			}
		}

		/// <summary>
		/// Converts the given sequence of attribute nodes.
		/// A function is given that can be used to handle 
		/// special cases. If such a special case has been 
		/// handled, the function returns 'true'. Only nodes
		/// for which the special case function returns 
		/// 'false', are directly converted by this node converter. 
		/// </summary>
		public IEnumerable<IAttribute> ConvertAttributeList(
			IEnumerable<LNode> Attributes, Func<LNode, bool> HandleSpecial, 
			GlobalScope Scope)
		{
			foreach (var item in Attributes)
			{
				if (!HandleSpecial(item))
				{
					var result = ConvertAttribute(item, Scope);
					if (result != null)
						yield return result;
				}
			}
		}

		/// <summary>
		/// Converts the given sequence of attribute nodes.
		/// A function is given that can be used to handle 
		/// special cases. If such a special case has been 
		/// handled, the function returns 'true'. Only nodes
		/// for which the special case function returns 
		/// 'false', are directly converted by this node converter. 
		/// Additionally, access modifiers are treated separately.
		/// </summary>
		public IEnumerable<IAttribute> ConvertAttributeListWithAccess(
			IEnumerable<LNode> Attributes, AccessModifier DefaultAccess,
			Func<LNode, bool> HandleSpecial, GlobalScope Scope)
		{
			var partitioned = NodeHelpers.Partition(Attributes, item => item.IsId && NodeHelpers.IsAccessModifier(item.Name));

			var accModSet = new HashSet<Symbol>();
			foreach (var item in partitioned.Item1)
			{
				var symbol = item.Name;
				if (!accModSet.Add(symbol) && EcsWarnings.DuplicateAccessModifierWarning.UseWarning(Scope.Log.Options))
				{
					// Looks like this access modifier is a duplicate.
					// Let's issue a warning.
					Scope.Log.LogWarning(new LogEntry(
						"duplicate access modifier",
						EcsWarnings.DuplicateAccessModifierWarning.CreateMessage(new MarkupNode(
							"#group",
							NodeHelpers.HighlightEven("access modifier '", symbol.Name, "' is duplicated. "))),
						NodeHelpers.ToSourceLocation(item.Range)));
				}
			}

			var accMod = NodeHelpers.ToAccessModifier(accModSet);
			if (!accMod.HasValue)
			{
				if (accModSet.Count != 0)
				{
					// The set of access modifiers we found was no good.
					// Throw an error in the user's direction.
					var first = partitioned.Item1.First();
					var srcLoc = NodeHelpers.ToSourceLocation(first.Range);
					var fragments = new List<string>();
					fragments.Add("set of access modifiers '");
					fragments.Add(first.Name.Name);
					foreach (var item in partitioned.Item1.Skip(1))
					{
						srcLoc = srcLoc.Concat(NodeHelpers.ToSourceLocation(item.Range));
						fragments.Add("', '");
						fragments.Add(item.Name.Name);
					}
					fragments.Add("' could not be unambiguously resolved.");
					Scope.Log.LogError(new LogEntry(
						"ambiguous access modifiers",
						NodeHelpers.HighlightEven(fragments.ToArray()),
						srcLoc));
				}

				// Assume that the default access modifier was intended.
				accMod = DefaultAccess;
			}

			yield return new AccessAttribute(accMod.Value);
			foreach (var item in ConvertAttributeList(partitioned.Item2, HandleSpecial, Scope))
				yield return item;
		}

		/// <summary>
		/// Converts the given expression node.
		/// </summary>
		public IExpression ConvertExpression(LNode Node, ILocalScope Scope)
		{
			var conv = GetConverterOrDefault(exprConverters, Node);
			if (conv == null)
			{
				if (Node.HasSpecialName || !Node.IsId)
				{
					LogCannotConvert(Node, Scope.Function.Global.Log);
					return VoidExpression.Instance;
				}
				else
				{
					var result = LookupUnqualifiedName(Node.Name.Name, Scope);
					if (result == null)
					{
						Scope.Function.Global.Log.LogError(new LogEntry(
							"undefined identifier",
							NodeHelpers.HighlightEven("identifier '", Node.Name.Name, "' was not defined in this scope."),
							NodeHelpers.ToSourceLocation(Node.Range)));
						return VoidExpression.Instance;
					}
					return result;
				}
			}
			else
			{
				return conv(Node, Scope, this);
			}
		}

		/// <summary>
		/// Registers a global converter.
		/// </summary>
		public void AddConverter(Symbol Symbol, GlobalConverter Converter)
		{
			globalConverters[Symbol] = Converter;
		}

		/// <summary>
		/// Registers a type member converter.
		/// </summary>
		public void AddConverter(Symbol Symbol, TypeMemberConverter Converter)
		{
			typeMemberConverters[Symbol] = Converter;
		}

		/// <summary>
		/// Registers a type converter.
		/// </summary>
		public void AddConverter(Symbol Symbol, TypeConverter Converter)
		{
			typeConverters[Symbol] = Converter;
		}

		/// <summary>
		/// Registers an attribute converter.
		/// </summary>
		public void AddConverter(Symbol Symbol, AttributeConverter Converter)
		{
			attrConverters[Symbol] = Converter;
		}

		/// <summary>
		/// Registers an expression converter.
		/// </summary>
		public void AddConverter(Symbol Symbol, ExpressionConverter Converter)
		{
			exprConverters[Symbol] = Converter;
		}

		/// <summary>
		/// Maps the given symbol to the given attribute.
		/// </summary>
		public void RegisterAttribute(Symbol Symbol, IAttribute Attribute)
		{
			AddConverter(Symbol, (node, scope, self) => Attribute);
		}

		/// <summary>
		/// Converts an entire compilation unit.
		/// </summary>
		public INamespaceBranch ConvertCompilationUnit(GlobalScope Scope, IAssembly DeclaringAssembly, IEnumerable<LNode> Nodes)
		{
			var rootNs = new RootNamespace(DeclaringAssembly);

			var state = Scope;
			foreach (var item in Nodes)
			{
				state = ConvertGlobal(item, rootNs, state);
			}

			return rootNs;
		}

		/// <summary>
		/// Gets the default node converter.
		/// </summary>
		public static NodeConverter DefaultNodeConverter
		{
			get
			{
				var result = new NodeConverter(ExpressionConverters.LookupUnqualifiedName);
				result.AddConverter(CodeSymbols.Import, GlobalConverters.ConvertImportDirective);
				result.AddConverter(CodeSymbols.Class, GlobalConverters.ConvertClassDefinition);
				result.AddConverter(CodeSymbols.Struct, GlobalConverters.ConvertStructDefinition);
				return result;
			}
		}
	}
}

