﻿using System;
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
	using TypeMemberConverter = Func<LNode, LazyDescribedType, GlobalScope, NodeConverter, GlobalScope>;
	using AttributeConverter = Func<LNode, GlobalScope, NodeConverter, IAttribute>;
	using ExpressionConverter = Func<LNode, LocalScope, NodeConverter, IExpression>;
	using TypeOrExpressionConverter = Func<LNode, LocalScope, NodeConverter, TypeOrExpression>;
	using LiteralConverter = Func<object, IExpression>;

	/// <summary>
	/// Defines a type that semantically analyzes a syntax tree by
	/// applying sub-converters to syntax nodes.
	/// </summary>
	public sealed class NodeConverter
	{
		public NodeConverter(
			Func<string, ILocalScope, TypeOrExpression> LookupUnqualifiedName,
			ExpressionConverter CallConverter)
		{
			this.LookupUnqualifiedName = LookupUnqualifiedName;
			this.CallConverter = CallConverter;
			this.globalConverters = new Dictionary<Symbol, GlobalConverter>();
			this.typeMemberConverters = new Dictionary<Symbol, TypeMemberConverter>();
			this.attrConverters = new Dictionary<Symbol, AttributeConverter>();
			this.exprConverters = new Dictionary<Symbol, TypeOrExpressionConverter>();
			this.literalConverters = new Dictionary<Type, LiteralConverter>();
		}

		private Dictionary<Symbol, GlobalConverter> globalConverters;
		private Dictionary<Symbol, TypeMemberConverter> typeMemberConverters;
		private Dictionary<Symbol, AttributeConverter> attrConverters;
		private Dictionary<Symbol, TypeOrExpressionConverter> exprConverters;
		private Dictionary<Type, LiteralConverter> literalConverters;

		public Func<string, ILocalScope, TypeOrExpression> LookupUnqualifiedName { get; private set; }
		public ExpressionConverter CallConverter { get; private set; }

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
			LNode Node, LazyDescribedType DeclaringType, GlobalScope Scope)
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
			var localScope = new LocalScope(
				new FunctionScope(Scope, null, null, new Dictionary<string, IVariable>()));

			return ConvertTypeOrExpression(Node, localScope).CollapseTypes(
				NodeHelpers.ToSourceLocation(Node.Range), Scope);
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
		/// Converts the given type-or-expression node.
		/// </summary>
		public TypeOrExpression ConvertTypeOrExpression(LNode Node, LocalScope Scope)
		{
			var conv = GetConverterOrDefault(exprConverters, Node);
			if (conv == null)
			{
				if (!Node.HasSpecialName)
				{
					if (Node.IsId)
					{
						var result = LookupUnqualifiedName(Node.Name.Name, Scope);
						return result.WithSourceLocation(NodeHelpers.ToSourceLocation(Node.Range));
					}
					else if (Node.IsCall)
					{
						return new TypeOrExpression(SourceExpression.Create(
								CallConverter(Node, Scope, this), NodeHelpers.ToSourceLocation(Node.Range)));
					}
					else if (Node.IsLiteral)
					{
						object val = Node.Value;
						LiteralConverter litConv;
						if (literalConverters.TryGetValue(val.GetType(), out litConv))
						{
							return new TypeOrExpression(
								SourceExpression.Create(litConv(val), NodeHelpers.ToSourceLocation(Node.Range)));
						}
						else
						{
							Scope.Function.Global.Log.LogError(new LogEntry(
								"unsupported literal type",
								NodeHelpers.HighlightEven(
									"literals of type '", val.GetType().FullName, "' are not supported."),
								NodeHelpers.ToSourceLocation(Node.Range)));
							return new TypeOrExpression(VoidExpression.Instance);
						}
					}
				}
				LogCannotConvert(Node, Scope.Function.Global.Log);
				return TypeOrExpression.Empty;
			}
			else
			{
				return conv(Node, Scope, this).WithSourceLocation(NodeHelpers.ToSourceLocation(Node.Range));
			}
		}

		/// <summary>
		/// Converts the given expression node.
		/// </summary>
		public IExpression ConvertExpression(LNode Node, LocalScope Scope)
		{
			var result = ConvertTypeOrExpression(Node, Scope);
			if (result.IsExpression)
			{
				return result.Expression;
			}
			else
			{
				Scope.Function.Global.Log.LogError(new LogEntry(
					"expression resolution",
					NodeHelpers.HighlightEven("expression could not be resolved."),
					NodeHelpers.ToSourceLocation(Node.Range)));
				return VoidExpression.Instance;
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
			AddConverter(Symbol, (node, scope, self) => 
				new TypeOrExpression(new IType[] 
				{ 
					Converter(node, scope.Function.Global, self)
				}));
		}

		/// <summary>
		/// Registers an attribute converter.
		/// </summary>
		public void AddConverter(Symbol Symbol, AttributeConverter Converter)
		{
			attrConverters[Symbol] = Converter;
		}

		/// <summary>
		/// Registers a type-or-expression converter.
		/// </summary>
		public void AddConverter(Symbol Symbol, TypeOrExpressionConverter Converter)
		{
			exprConverters[Symbol] = Converter;
		}

		/// <summary>
		/// Registers an expression converter.
		/// </summary>
		public void AddConverter(Symbol Symbol, ExpressionConverter Converter)
		{
			exprConverters[Symbol] = (node, scope, self) => new TypeOrExpression(Converter(node, scope, self));
		}

		/// <summary>
		/// Registers a literal converter.
		/// </summary>
		public void AddConverter(Type LiteralType, LiteralConverter Converter)
		{
			literalConverters[LiteralType] = Converter;
		}

		/// <summary>
		/// Registers a literal converter.
		/// </summary>
		public void AddConverter<T>(Func<T, IExpression> Converter)
		{
			AddConverter(typeof(T), val => Converter((T)val));
		}

		/// <summary>
		/// Maps the given symbol to the given attribute.
		/// </summary>
		public void AliasAttribute(Symbol Symbol, IAttribute Attribute)
		{
			AddConverter(Symbol, (node, scope, self) => Attribute);
		}

		/// <summary>
		/// Maps the given symbol to the given type.
		/// </summary>
		public void AliasType(Symbol Symbol, IType Type)
		{
			AddConverter(Symbol, (node, scope, self) => Type);
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
				var result = new NodeConverter(
					ExpressionConverters.LookupUnqualifiedName,
					ExpressionConverters.ConvertCall);

				// Global entities
				result.AddConverter(CodeSymbols.Import, GlobalConverters.ConvertImportDirective);
				result.AddConverter(CodeSymbols.Class, GlobalConverters.ConvertClassDefinition);
				result.AddConverter(CodeSymbols.Struct, GlobalConverters.ConvertStructDefinition);

				// Type members
				result.AddConverter(CodeSymbols.Fn, TypeMemberConverters.ConvertFunction);

				// Expressions
				result.AddConverter(CodeSymbols.Braces, ExpressionConverters.ConvertBlock);
				result.AddConverter(CodeSymbols.Return, ExpressionConverters.ConvertReturn);
				result.AddConverter(CodeSymbols.Dot, ExpressionConverters.ConvertMemberAccess);

				// Literals
				result.AddConverter<sbyte>(val => new Int8Expression(val));
				result.AddConverter<short>(val => new Int16Expression(val));
				result.AddConverter<int>(val => new Int32Expression(val));
				result.AddConverter<long>(val => new Int64Expression(val));
				result.AddConverter<byte>(val => new UInt8Expression(val));
				result.AddConverter<ushort>(val => new UInt16Expression(val));
				result.AddConverter<uint>(val => new UInt32Expression(val));
				result.AddConverter<ulong>(val => new UInt64Expression(val));
				result.AddConverter<float>(val => new Float32Expression(val));
				result.AddConverter<double>(val => new Float64Expression(val));
				result.AddConverter<bool>(val => new BooleanExpression(val));
				result.AddConverter<char>(val => new CharExpression(val));
				result.AddConverter<string>(val => new StringExpression(val));

				// Primitive types
				result.AliasType(CodeSymbols.Int8, PrimitiveTypes.Int8);
				result.AliasType(CodeSymbols.Int16, PrimitiveTypes.Int16);
				result.AliasType(CodeSymbols.Int32, PrimitiveTypes.Int32);
				result.AliasType(CodeSymbols.Int64, PrimitiveTypes.Int64);
				result.AliasType(CodeSymbols.UInt8, PrimitiveTypes.UInt8);
				result.AliasType(CodeSymbols.UInt16, PrimitiveTypes.UInt16);
				result.AliasType(CodeSymbols.UInt32, PrimitiveTypes.UInt32);
				result.AliasType(CodeSymbols.UInt64, PrimitiveTypes.UInt64);
				result.AliasType(CodeSymbols.Single, PrimitiveTypes.Float32);
				result.AliasType(CodeSymbols.Double, PrimitiveTypes.Float64);
				result.AliasType(CodeSymbols.Char, PrimitiveTypes.Char);
				result.AliasType(CodeSymbols.Bool, PrimitiveTypes.Boolean);
				result.AliasType(CodeSymbols.String, PrimitiveTypes.String);
				result.AliasType(CodeSymbols.Void, PrimitiveTypes.Void);

				return result;
			}
		}
	}
}

