using System;
using System.Collections.Generic;
using System.Linq;
using Flame.Collections;
using Flame.Compiler;
using Flame.Compiler.Statements;
using Flame.Compiler.Variables;
using Pixie;
using Loyc;

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
        /// Gets the enclosing control-flow node's
        /// tag, if there is an enclosing control-flow node.
        /// Otherwise, null is returned.
        /// </summary>
        /// <remarks>
        /// This tag can be used as a target for break
        /// and continue nodes.
        /// </remarks>
        UniqueTag FlowTag { get; }

        /// <summary>
        /// Gets this local scope's return type.
        /// </summary>
        /// <value>The type of the return value.</value>
        IType ReturnType { get; }

        /// <summary>
        /// Gets the set of all local variable identifiers
        /// that are defined in this scope.
        /// </summary>
        IEnumerable<Symbol> VariableNames { get; }

        /// <summary>
        /// Gets the variable with the given name.
        /// </summary>
        IVariable GetVariable(Symbol Name);
    }

    /// <summary>
    /// A data structure that represents a simple local scope.
    /// </summary>
    public sealed class LocalScope : ILocalScope
    {
        public LocalScope(ILocalScope Parent)
            : this(
                Parent, new List<IVariable>(), 
                new Dictionary<Symbol, IVariable>(),
                new Dictionary<Symbol, IVariableMember>())
        {
        }

        private LocalScope(
            ILocalScope Parent, List<IVariable> OrderedVars,
            Dictionary<Symbol, IVariable> Locals,
            Dictionary<Symbol, IVariableMember> LocalMembers)
        {
            this.Parent = Parent;
            this.orderedVars = OrderedVars;
            this.locals = Locals;
            this.localMembers = LocalMembers;
        }

        private List<IVariable> orderedVars;
        private Dictionary<Symbol, IVariable> locals;
        private Dictionary<Symbol, IVariableMember> localMembers;

        /// <summary>
        /// Gets this local scope's parent scope.
        /// </summary>
        public ILocalScope Parent { get; private set; }

        /// <summary>
        /// Gets this local scope's function scope.
        /// </summary>
        public FunctionScope Function { get { return Parent.Function; } }

        /// <summary>
        /// Gets the log object for this scope.
        /// </summary>
        public ICompilerLog Log { get { return Function.Global.Log; } }

        /// <inheritdoc/>
        public UniqueTag FlowTag { get { return Parent.FlowTag; } }

        /// <summary>
        /// Gets this local scope's return type.
        /// </summary>
        /// <value>The type of the return value.</value>
        public IType ReturnType
        {
            get { return Parent.ReturnType; }
        }

        /// <summary>
        /// Gets the set of all local variable identifiers
        /// that are defined in this scope.
        /// </summary>
        public IEnumerable<Symbol> VariableNames 
        { 
            get 
            { 
                return LocalVariableNames.Union(Parent.VariableNames); 
            } 
        }

        /// <summary>
        /// Gets the set of locally defined variable identifiers
        /// for this scope.
        /// </summary>
        public IEnumerable<Symbol> LocalVariableNames 
        { 
            get { return locals.Keys; } 
        }

        /// <summary>
        /// Creates a statement that releases 
        /// resources consumed by this local scope.
        /// </summary>
        public IStatement Release()
        {
            var cleanup = new List<IStatement>();
            foreach (var item in orderedVars)
                cleanup.Add(item.CreateReleaseStatement());
            return new BlockStatement(cleanup);
        }

        /// <summary>
        /// Declares a local variable with the given
        /// name and signature.
        /// </summary>
        public IVariable DeclareLocal(Symbol Name, IVariableMember Member)
        {
            return DeclareLocal(Name, Member, new LocalVariable(Member, new UniqueTag(Name.Name)));
        }

        /// <summary>
        /// Declares a local variable with the given
        /// name and signature.
        /// </summary>
        public IVariable DeclareLocal(Symbol Name, IVariableMember Member, IVariable Variable)
        {
            if (locals.ContainsKey(Name))
            {
                // Variable was already declared in this scope.
                // Log an error.
                Function.Global.Log.LogError(new LogEntry(
                    "variable redefinition",
                    new MarkupNode[]
                    {
                        new MarkupNode("#group", NodeHelpers.HighlightEven("variable '", Name.Name, "' is defined more than once in the same scope.")),
                        Member.GetSourceLocation().CreateDiagnosticsNode(),
                        localMembers[Name].GetSourceLocation().CreateRemarkDiagnosticsNode("previous declaration: ")
                    }));
            }
            else if (Parent.GetVariable(Name) != null
            && Function.Global.UseWarning(Warnings.Instance.Shadow))
            {
                // Variable was already declared by parent scope.
                // Log a warning.
                var shadowedVar = Parent.GetVariable(Name) as LocalVariableBase;
                var nodes = new List<MarkupNode>();
                nodes.Add(Warnings.Instance.Shadow.CreateMessage(
                    new MarkupNode("#group", 
                        NodeHelpers.HighlightEven(
                            "variable '", Name.Name, 
                            "' is defined more than once in the same scope. "))));
                nodes.Add(Member.GetSourceLocation().CreateDiagnosticsNode());
                if (shadowedVar != null)
                {
                    nodes.Add(
                        shadowedVar.Member.GetSourceLocation()
                        .CreateRemarkDiagnosticsNode("shadowed declaration: "));
                }
                Function.Global.Log.LogWarning(new LogEntry(
                    "variable shadowed", nodes));
            }

            orderedVars.Add(Variable);
            locals[Name] = Variable;
            localMembers[Name] = Member;
            return Variable;
        }

        /// <summary>
        /// Gets the variable with the given name.
        /// </summary>
        public IVariable GetVariable(Symbol Name)
        {
            IVariable result;
            if (locals.TryGetValue(Name, out result))
                return result;
            else
                return Parent.GetVariable(Name);
        }
    }

    /// <summary>
    /// A data structure that represents a local scope for a 
    /// control-flow node.
    /// </summary>
    public sealed class FlowScope : ILocalScope
    {
        public FlowScope(ILocalScope Parent, UniqueTag FlowTag)
        {
            this.Parent = Parent;
            this.FlowTag = FlowTag;
        }

        /// <summary>
        /// Gets this local scope's parent scope.
        /// </summary>
        public ILocalScope Parent { get; private set; }

        /// <inheritdoc/>
        public UniqueTag FlowTag { get; private set; }

        /// <summary>
        /// Gets this local scope's function scope.
        /// </summary>
        public FunctionScope Function { get { return Parent.Function; } }

        /// <summary>
        /// Gets the log object for this scope.
        /// </summary>
        public ICompilerLog Log { get { return Function.Global.Log; } }

        /// <summary>
        /// Gets this local scope's return type.
        /// </summary>
        /// <value>The type of the return value.</value>
        public IType ReturnType
        {
            get { return Parent.ReturnType; }
        }

        /// <summary>
        /// Gets the set of all local variable identifiers
        /// that are defined in this scope.
        /// </summary>
        public IEnumerable<Symbol> VariableNames { get { return Parent.VariableNames; } }

        /// <summary>
        /// Gets the variable with the given name.
        /// </summary>
        public IVariable GetVariable(Symbol Name)
        {
            return Parent.GetVariable(Name);
        }
    }

    /// <summary>
    /// A local scope that replaces the old function scope by a new function scope.
    /// </summary>
    public sealed class AlteredFunctionScope : ILocalScope
    {
        public AlteredFunctionScope(ILocalScope Parent, FunctionScope Function)
        {
            this.Parent = Parent;
            this.Function = Function;
        }

        /// <summary>
        /// Gets this local scope's parent scope.
        /// </summary>
        public ILocalScope Parent { get; private set; }

        /// <summary>
        /// Gets this local scope's function scope.
        /// </summary>
        public FunctionScope Function { get; private set; }

        /// <inheritdoc/>
        public UniqueTag FlowTag { get { return Parent.FlowTag; } }

        /// <summary>
        /// Gets the log object for this scope.
        /// </summary>
        public ICompilerLog Log { get { return Function.Global.Log; } }

        /// <summary>
        /// Gets this local scope's return type.
        /// </summary>
        /// <value>The type of the return value.</value>
        public IType ReturnType
        {
            get { return Parent.ReturnType; }
        }

        /// <summary>
        /// Gets the set of all local variable identifiers
        /// that are defined in this scope.
        /// </summary>
        public IEnumerable<Symbol> VariableNames { get { return Parent.VariableNames; } }

        /// <summary>
        /// Gets the variable with the given name.
        /// </summary>
        public IVariable GetVariable(Symbol Name)
        {
            return Parent.GetVariable(Name);
        }
    }
}

