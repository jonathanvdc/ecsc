using System;
using System.Collections.Generic;
using System.Linq;
using Flame.Compiler;
using Flame.Compiler.Statements;
using Pixie;
using Flame.Compiler.Variables;

namespace Flame.Ecs
{
	/// <summary>
	/// Defines a common interface for local scope data structures.
	/// </summary>
	public interface ILocalScope
	{
		/// <summary>
		/// Gets this local scope's function scope.
		/// </summary>
		FunctionScope Function { get; }

		/// <summary>
		/// Gets the set of all local variable identifiers
		/// that are defined in this scope.
		/// </summary>
		IEnumerable<string> VariableNames { get; }

		/// <summary>
		/// Gets the variable with the given name.
		/// </summary>
		IVariable GetVariable(string Name);

		/// <summary>
		/// Declares a local variable with the given
		/// name and signature. A new local scope is 
		/// created to accomodate it, and that scope
		/// is returned.
		/// </summary>
		ILocalScope DeclareLocal(string Name, IVariableMember Member);

		/// <summary>
		/// Creates a statement that releases 
		/// resources consumed by this local scope.
		/// </summary>
		IStatement Release();
	}

	/// <summary>
	/// A data structure that represents the top-level scope
	/// of a function-like member. An enclosing type and
	/// a sequence of parameter variables are stored, but
	/// no local variables can be declared in this type of scope. 
	/// </summary>
	public sealed class FunctionScope : ILocalScope
	{
		public FunctionScope(
			GlobalScope Global, IType CurrentType, 
			IReadOnlyDictionary<string, IVariable> ParameterVariables)
		{
			this.Global = Global;
			this.CurrentType = CurrentType;
			this.ParameterVariables = ParameterVariables;
		}

		/// <summary>
		/// Gets this function scope's enclosing global scope.
		/// </summary>
		/// <value>The global scope.</value>
		public GlobalScope Global { get; private set; }

		/// <summary>
		/// Gets the type of a hypothetical 'this' pointer:
		/// the enclosing type, optionally instantiated by
		/// its own generic parameters.
		/// </summary>
		public IType CurrentType { get; private set; }

		/// <summary>
		/// Gets a read-only dictionary that maps identifiers
		/// to parameter variables.
		/// </summary>
		/// <value>The parameter variables dictionary.</value>
		public IReadOnlyDictionary<string, IVariable> ParameterVariables { get; private set; }

		/// <summary>
		/// Gets this local scope's function scope.
		/// </summary>
		public FunctionScope Function { get { return this; } }

		/// <summary>
		/// Gets the set of all local variable identifiers
		/// that are defined in this scope.
		/// </summary>
		public IEnumerable<string> VariableNames { get { return ParameterVariables.Keys; } }

		/// <summary>
		/// Creates a statement that releases 
		/// resources consumed by this local scope.
		/// </summary>
		public IStatement Release()
		{
			// Nothing to release here.
			return EmptyStatement.Instance;
		}

		/// <summary>
		/// Gets the variable with the given name.
		/// </summary>
		public IVariable GetVariable(string Name)
		{
			IVariable result;
			if (ParameterVariables.TryGetValue(Name, out result))
				return result;
			else
				return null;
		}

		/// <summary>
		/// Declares a local variable with the given
		/// name and signature. A new local scope is 
		/// created to accomodate it, and that scope
		/// is returned.
		/// </summary>
		public ILocalScope DeclareLocal(string Name, IVariableMember Member)
		{
			return new LocalScope(this).DeclareLocal(Name, Member);
		}
	}

	/// <summary>
	/// A data structure that represents a simple local scope.
	/// </summary>
	public class LocalScope : ILocalScope
	{
		public LocalScope(ILocalScope Parent)
			: this(Parent, new List<LocalVariable>(), new Dictionary<string, LocalVariable>())
		{ }

		private LocalScope(
			ILocalScope Parent, List<LocalVariable> OrderedVars,
			Dictionary<string, LocalVariable> Locals)
		{
			this.Parent = Parent;
			this.orderedVars = OrderedVars;
			this.locals = Locals;
		}

		private List<LocalVariable> orderedVars;
		private Dictionary<string, LocalVariable> locals;

		/// <summary>
		/// Gets this local scope's parent scope.
		/// </summary>
		public ILocalScope Parent { get; private set; }

		/// <summary>
		/// Gets this local scope's function scope.
		/// </summary>
		public FunctionScope Function { get { return Parent.Function; } }

		/// <summary>
		/// Gets the set of all local variable identifiers
		/// that are defined in this scope.
		/// </summary>
		public IEnumerable<string> VariableNames { get { return locals.Keys.Union(Parent.VariableNames); } }

		/// <summary>
		/// Creates a statement that releases 
		/// resources consumed by this local scope.
		/// </summary>
		public IStatement Release()
		{
			var cleanup = new List<IStatement>();
			foreach (var item in locals)
				cleanup.Add(item.Value.CreateReleaseStatement());
			return new BlockStatement(cleanup);
		}

		/// <summary>
		/// Declares a local variable with the given
		/// name and signature. A new local scope is 
		/// created to accomodate it, and that scope
		/// is returned.
		/// </summary>
		public ILocalScope DeclareLocal(string Name, IVariableMember Member)
		{
			if (locals.ContainsKey(Name))
			{
				// Variable was already declared in this scope.
				// Log an error.
				Function.Global.Log.LogError(new LogEntry(
					"variable redefinition",
					new MarkupNode[] 
					{
						new MarkupNode("#group", NodeHelpers.HighlightEven("variable '", Name, "' is defined more than once in the same scope.")),
						locals[Name].Member.GetSourceLocation().CreateRemarkDiagnosticsNode("previous declaration: "),
						Member.GetSourceLocation().CreateDiagnosticsNode()
					}));
			}
			else if (Parent.GetVariable(Name) != null 
				&& Warnings.Instance.Shadow.UseWarning(Function.Global.Log.Options))
			{
				// Variable was already declared by parent scope.
				// Log a warning.
				Function.Global.Log.LogWarning(new LogEntry(
					"variable shadowed",
					new MarkupNode[] 
					{
						Warnings.Instance.Shadow.CreateMessage(new MarkupNode("#group", NodeHelpers.HighlightEven("variable '", Name, "' is defined more than once in the same scope. "))),
						locals[Name].Member.GetSourceLocation().CreateRemarkDiagnosticsNode("shadowed declaration: "),
						Member.GetSourceLocation().CreateDiagnosticsNode()
					}));
			}

			var localVar = new LocalVariable(Member, new UniqueTag(Name));
			var newOrdVars = new List<LocalVariable>(orderedVars);
			newOrdVars.Add(localVar);
			var newVarDict = new Dictionary<string, LocalVariable>(locals);
			newVarDict[Name] = localVar;
			return new LocalScope(Parent, newOrdVars, newVarDict);
		}

		/// <summary>
		/// Gets the variable with the given name.
		/// </summary>
		public IVariable GetVariable(string Name)
		{
			LocalVariable result;
			if (locals.TryGetValue(Name, out result))
				return result;
			else
				return Parent.GetVariable(Name);
		}
	}
}

