using System;
using System.Collections.Generic;
using System.Linq;
using Flame.Collections;
using Flame.Compiler;
using Flame.Compiler.Statements;
using Flame.Compiler.Variables;
using Pixie;

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
        IEnumerable<string> VariableNames { get; }

        /// <summary>
        /// Gets the variable with the given name.
        /// </summary>
        IVariable GetVariable(string Name);

        /// <summary>
        /// Gets the stashed variable with the given name,
        /// at the given stash-stack depth. Null is returned
        /// if there is no such variable.
        /// </summary>
        /// <returns>The stashed variable.</returns>
        /// <param name="Name">The stashed variable's name.</param>
        /// <param name="StackDepth">The stack depth of the variable in the stash.</param>
        IVariable GetStashedVariable(string Name, int StackDepth);
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
            IMethod CurrentMethod, IType ReturnType,
            IReadOnlyDictionary<string, IVariable> ParameterVariables)
        {
            this.instanceMemberCache = new Dictionary<Tuple<IType, string>, ITypeMember[]>();
            this.staticMemberCache = new Dictionary<Tuple<IType, string>, ITypeMember[]>();
            this.instanceIndexerCache = new Dictionary<IType, IProperty[]>();
            this.operatorCache = new Dictionary<IType, SmallMultiDictionary<Operator, IMethod>>();

            this.Global = Global;
            this.CurrentType = CurrentType;
            this.DeclaringType = CurrentType != null && CurrentType.GetIsPointer() 
                ? CurrentType.AsPointerType().ElementType
                : CurrentType;
            this.CurrentMethod = CurrentMethod;
            this.ReturnType = ReturnType;
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
        /// its own generic parameters. Additionally, 
        /// value types have a pointer current type.
        /// </summary>
        public IType CurrentType { get; private set; }

        /// <summary>
        /// Gets the type of a hypothetical 'this' expression:
        /// the enclosing type, optionally instantiated by
        /// its own generic parameters. Value types do not
        /// have a pointer declaring type.
        /// </summary>
        public IType DeclaringType { get; private set; }

        /// <summary>
        /// Gets the enclosing method. This may be null if there is
        /// no enclosing method.
        /// </summary>
        public IMethod CurrentMethod { get; private set; }

        /// <summary>
        /// Gets the return type of this scope.
        /// </summary>
        public IType ReturnType { get; private set; }

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

        /// <inheritdoc/>
        public UniqueTag FlowTag { get { return null; } }

        /// <summary>
        /// Gets the set of all local variable identifiers
        /// that are defined in this scope.
        /// </summary>
        public IEnumerable<string> VariableNames { get { return ParameterVariables.Keys; } }

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

        /// <inheritdoc/>
        public IVariable GetStashedVariable(string Name, int StackDepth)
        {
            return null;
        }

        private Dictionary<Tuple<IType, string>, ITypeMember[]> instanceMemberCache;
        private Dictionary<Tuple<IType, string>, ITypeMember[]> staticMemberCache;
        private Dictionary<IType, IProperty[]> instanceIndexerCache;
        private Dictionary<IType, SmallMultiDictionary<Operator, IMethod>> operatorCache;

        private ITypeMember[] GetMembers(
            IType Type, string Name, 
            Dictionary<Tuple<IType, string>, ITypeMember[]> MemberCache,
            Func<ITypeMember, bool> Predicate)
        {
            var key = Tuple.Create(Type, Name);
            ITypeMember[] result;
            if (MemberCache.TryGetValue(key, out result))
            {
                return result;
            }
            else
            {
                result = Global.MemberCache.GetAllMembers(Type, Name)
                    .Where(Predicate)
                    .ToArray();
                MemberCache[key] = result;
                return result;
            }
        }

        /// <summary>
        /// Gets all indexers that can be accessed on an instance
        /// of the given type.
        /// </summary>
        public IEnumerable<IProperty> GetInstanceIndexers(IType Type)
        {
            IProperty[] result;
            if (instanceIndexerCache.TryGetValue(Type, out result))
            {
                return result;
            }
            else
            {
                result = Global.MemberCache.GetAllIndexers(Type)
                    .Where(p => 
                        !p.IsStatic && DeclaringType.CanAccess(p))
                    .ToArray();
                instanceIndexerCache[Type] = result;
                return result;
            }
        }

        /// <summary>
        /// Gets all members with the given name that can be accessed
        /// on an instance of the given type. 
        /// </summary>
        public IEnumerable<ITypeMember> GetInstanceMembers(IType Type, string Name)
        {
            return GetMembers(Type, Name, instanceMemberCache, item =>
            {
                return !item.IsStatic && DeclaringType.CanAccess(item);
            });
        }

        /// <summary>
        /// Gets all members with the given name that can be accessed
        /// on the given type's name.
        /// </summary>
        public IEnumerable<ITypeMember> GetStaticMembers(IType Type, string Name)
        {
            return GetMembers(Type, Name, staticMemberCache, item =>
            {
                return item.IsStatic && DeclaringType.CanAccess(item);
            });
        }

        /// <summary>
        /// Gets all static members with the given name that can be 
        /// found via unqualified name lookup.
        /// </summary>
        public IEnumerable<ITypeMember> GetUnqualifiedStaticMembers(string Name)
        {
            var results = new HashSet<ITypeMember>();
            if (DeclaringType != null)
                results.UnionWith(GetStaticMembers(DeclaringType, Name));

            foreach (var ty in Global.Binder.TypeUsings)
            {
                foreach (var method in GetStaticMembers(ty, Name))
                {
                    if (!method.GetIsExtension())
                        results.Add(method);
                }
            }

            return results;
        }

        /// <summary>
        /// Gets all operators defined by the given type (or one of its ancestors) 
        /// that can be accessed on the given type's name.
        /// </summary>
        public IEnumerable<IMethod> GetOperators(IType Type, Operator Op)
        {
            SmallMultiDictionary<Operator, IMethod> result;
            if (operatorCache.TryGetValue(Type, out result))
            {
                return result.GetAll(Op);
            }
            else
            {
                var globalOps = Global.MemberCache.GetAllOperators(Type);
                var localOps = new SmallMultiDictionary<Operator, IMethod>(globalOps.Count);
                foreach (var item in globalOps.Values)
                {
                    if (DeclaringType.CanAccess(item))
                        localOps.Add(item.GetOperator(), item);
                }
                operatorCache[Type] = localOps;
                return localOps.GetAll(Op);
            }
        }
    }

    /// <summary>
    /// A data structure that represents a simple local scope.
    /// </summary>
    public sealed class LocalScope : ILocalScope
    {
        public LocalScope(ILocalScope Parent)
            : this(
                Parent, new List<IVariable>(), 
                new Dictionary<string, IVariable>(),
                new Dictionary<string, IVariableMember>())
        {
        }

        private LocalScope(
            ILocalScope Parent, List<IVariable> OrderedVars,
            Dictionary<string, IVariable> Locals,
            Dictionary<string, IVariableMember> LocalMembers)
        {
            this.Parent = Parent;
            this.orderedVars = OrderedVars;
            this.locals = Locals;
            this.localMembers = LocalMembers;
        }

        private List<IVariable> orderedVars;
        private Dictionary<string, IVariable> locals;
        private Dictionary<string, IVariableMember> localMembers;

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
        public IEnumerable<string> VariableNames 
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
        public IEnumerable<string> LocalVariableNames 
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
        public IVariable DeclareLocal(string Name, IVariableMember Member)
        {
            return DeclareLocal(Name, Member, new LocalVariable(Member, new UniqueTag(Name)));
        }

        /// <summary>
        /// Declares a local variable with the given
        /// name and signature.
        /// </summary>
        public IVariable DeclareLocal(string Name, IVariableMember Member, IVariable Variable)
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
                        Member.GetSourceLocation().CreateDiagnosticsNode(),
                        localMembers[Name].GetSourceLocation().CreateRemarkDiagnosticsNode("previous declaration: ")
                    }));
            }
            else if (Parent.GetVariable(Name) != null
            && Warnings.Instance.Shadow.UseWarning(Function.Global.Log.Options))
            {
                // Variable was already declared by parent scope.
                // Log a warning.
                var shadowedVar = Parent.GetVariable(Name) as LocalVariableBase;
                var nodes = new List<MarkupNode>();
                nodes.Add(Warnings.Instance.Shadow.CreateMessage(
                    new MarkupNode("#group", 
                        NodeHelpers.HighlightEven(
                            "variable '", Name, 
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
        public IVariable GetVariable(string Name)
        {
            IVariable result;
            if (locals.TryGetValue(Name, out result))
                return result;
            else
                return Parent.GetVariable(Name);
        }

        /// <inheritdoc/>
        public IVariable GetStashedVariable(string Name, int StackDepth)
        {
            return Parent.GetStashedVariable(Name, StackDepth);
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
        public IEnumerable<string> VariableNames { get { return Parent.VariableNames; } }

        /// <summary>
        /// Gets the variable with the given name.
        /// </summary>
        public IVariable GetVariable(string Name)
        {
            return Parent.GetVariable(Name);
        }

        /// <inheritdoc/>
        public IVariable GetStashedVariable(string Name, int StackDepth)
        {
            return Parent.GetStashedVariable(Name, StackDepth);
        }
    }

    /// <summary>
    /// A data structure that represents a local scope that stashes
    /// a number of variables.
    /// </summary>
    public sealed class StashScope : ILocalScope
    {
        public StashScope(
            ILocalScope Parent, IEnumerable<string> StashedNames)
        {
            this.Parent = Parent;
            this.stashedNameSet = new HashSet<string>(StashedNames);
        }

        private HashSet<string> stashedNameSet;

        /// <summary>
        /// Gets this local scope's parent scope.
        /// </summary>
        public ILocalScope Parent { get; private set; }

        /// <inheritdoc/>
        public UniqueTag FlowTag { get { return Parent.FlowTag; } }

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
        public IEnumerable<string> VariableNames { get { return Parent.VariableNames.Except(stashedNameSet); } }

        /// <summary>
        /// Gets the variable with the given name.
        /// </summary>
        public IVariable GetVariable(string Name)
        {
            if (stashedNameSet.Contains(Name))
                return null;
            else
                return Parent.GetVariable(Name);
        }

        /// <inheritdoc/>
        public IVariable GetStashedVariable(string Name, int StackDepth)
        {
            if (!stashedNameSet.Contains(Name))
                return Parent.GetStashedVariable(Name, StackDepth);
            else if (StackDepth == 0)
                return Parent.GetVariable(Name);
            else
                return Parent.GetStashedVariable(Name, StackDepth - 1);
        }
    }

    /// <summary>
    /// A data structure that represents a local scope that restores
    /// a number of stashed variables.
    /// </summary>
    public sealed class RestoreScope : ILocalScope
    {
        public RestoreScope(
            ILocalScope Parent, IEnumerable<string> RestoredNames)
        {
            this.Parent = Parent;
            this.restoredNameSet = new HashSet<string>(RestoredNames);
        }

        private HashSet<string> restoredNameSet;

        /// <summary>
        /// Gets this local scope's parent scope.
        /// </summary>
        public ILocalScope Parent { get; private set; }

        /// <inheritdoc/>
        public UniqueTag FlowTag { get { return Parent.FlowTag; } }

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
        public IEnumerable<string> VariableNames { get { return Parent.VariableNames.Union(restoredNameSet); } }

        /// <summary>
        /// Gets the variable with the given name.
        /// </summary>
        public IVariable GetVariable(string Name)
        {
            if (restoredNameSet.Contains(Name))
                return Parent.GetStashedVariable(Name, 0);
            else
                return Parent.GetVariable(Name);
        }

        /// <inheritdoc/>
        public IVariable GetStashedVariable(string Name, int StackDepth)
        {
            if (restoredNameSet.Contains(Name))
                // Stashed variables are stored in a stack of sorts.
                // Since this RestoreScope "pops" a stashed variable
                // from this stack, we must look further, which we can
                // accomplish by incrementing the stack depth.
                return Parent.GetStashedVariable(Name, StackDepth + 1);
            else
                return Parent.GetStashedVariable(Name, StackDepth);
        }
    }
}

