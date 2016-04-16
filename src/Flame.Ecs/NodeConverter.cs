using System;
using System.Collections.Generic;
using Loyc.Syntax;
using Loyc;
using Flame.Compiler;

namespace Flame.Ecs
{
	using GlobalConverter = Func<LNode, IMutableNamespace, GlobalScope, GlobalScope>;

	/// <summary>
	/// Defines a type that semantically analyzes a syntax tree by
	/// applying sub-converters to syntax nodes.
	/// </summary>
	public class NodeConverter
	{
		public NodeConverter()
		{
			this.globalConverters = new Dictionary<Symbol, GlobalConverter>();
		}

		private Dictionary<Symbol, GlobalConverter> globalConverters;

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
					"' could not be converted because its type was not recognized " +
					"as a known type."),
				NodeHelpers.ToSourceLocation(Node.Range)));
		}

		/// <summary>
		/// Converts a global node.
		/// </summary>
		private GlobalScope ConvertGlobal(
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
				return conv(Node, Namespace, Scope);
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
				return result;
			}
		}
	}
}

