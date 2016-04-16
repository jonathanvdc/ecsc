using System;
using System.Collections.Generic;
using Loyc.Syntax;
using Loyc;
using Flame.Compiler;
using Flame.Build;

namespace Flame.Ecs
{
	using GlobalConverter = Func<LNode, IMutableNamespace, GlobalScope, NodeConverter, GlobalScope>;
	using TypeConverter = Func<LNode, GlobalScope, NodeConverter, IType>;
	using TypeMemberConverter = Func<LNode, DescribedType, GlobalScope, NodeConverter, GlobalScope>;

	/// <summary>
	/// Defines a type that semantically analyzes a syntax tree by
	/// applying sub-converters to syntax nodes.
	/// </summary>
	public class NodeConverter
	{
		public NodeConverter()
		{
			this.globalConverters = new Dictionary<Symbol, GlobalConverter>();
			this.typeConverters = new Dictionary<Symbol, TypeConverter>();
			this.typeMemberConverters = new Dictionary<Symbol, TypeMemberConverter>();
		}

		private Dictionary<Symbol, GlobalConverter> globalConverters;
		private Dictionary<Symbol, TypeConverter> typeConverters;
		private Dictionary<Symbol, TypeMemberConverter> typeMemberConverters;

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
					"as a known node type (in this context)."),
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
				var result = new NodeConverter();
				result.AddConverter(CodeSymbols.Import, GlobalConverters.ConvertImportDirective);
				result.AddConverter(CodeSymbols.Class, GlobalConverters.ConvertClassDefinition);
				result.AddConverter(CodeSymbols.Struct, GlobalConverters.ConvertStructDefinition);
				return result;
			}
		}
	}
}

