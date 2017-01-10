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
            IReadOnlyDictionary<Symbol, IVariable> ParameterVariables)
        {
            this.instanceMemberCache = new Dictionary<Tuple<IType, string>, ITypeMember[]>();
            this.extensionMemberCache = new Dictionary<Tuple<IType, string>, ITypeMember[]>();
            this.staticMemberCache = new Dictionary<Tuple<IType, string>, ITypeMember[]>();
            this.instanceCtorCache = new Dictionary<IType, IMethod[]>();
            this.instanceIndexerCache = new Dictionary<IType, IProperty[]>();
            this.operatorCache = new Dictionary<IType, SmallMultiDictionary<Operator, IMethod>>();

            this.Global = Global;
            this.CurrentType = CurrentType;
            this.DeclaringType = DereferenceOrId(CurrentType);
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
        public IReadOnlyDictionary<Symbol, IVariable> ParameterVariables { get; private set; }

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
        public IEnumerable<Symbol> VariableNames { get { return ParameterVariables.Keys; } }

        /// <summary>
        /// Gets the variable with the given name.
        /// </summary>
        public IVariable GetVariable(Symbol Name)
        {
            IVariable result;
            if (ParameterVariables.TryGetValue(Name, out result))
                return result;
            else
                return null;
        }

        private Dictionary<Tuple<IType, string>, ITypeMember[]> instanceMemberCache;
        private Dictionary<Tuple<IType, string>, ITypeMember[]> extensionMemberCache;
        private Dictionary<Tuple<IType, string>, ITypeMember[]> staticMemberCache;
        private Dictionary<IType, IProperty[]> instanceIndexerCache;
        private Dictionary<IType, IMethod[]> instanceCtorCache;
        private Dictionary<IType, SmallMultiDictionary<Operator, IMethod>> operatorCache;

        private ITypeMember[] GetMembers(
            IType Type, string Name, 
            Dictionary<Tuple<IType, string>, ITypeMember[]> TargetCache,
            Func<ITypeMember, bool> Predicate)
        {
            return GetMembers(Type, Name, TargetCache, Global.MemberCache, Predicate);
        }

        private ITypeMember[] GetMembers(
            IType Type, string Name, 
            Dictionary<Tuple<IType, string>, ITypeMember[]> TargetCache,
            MemberCacheBase SourceCache,
            Func<ITypeMember, bool> Predicate)
        {
            var key = Tuple.Create(Type, Name);
            ITypeMember[] result;
            if (TargetCache.TryGetValue(key, out result))
            {
                return result;
            }
            else
            {
                result = SourceCache.GetAllMembers(Type, Name)
                    .Where(Predicate)
                    .ToArray();
                TargetCache[key] = result;
                return result;
            }
        }

        /// <summary>
        /// Determines whether this instance can access the specified member.
        /// </summary>
        /// <returns><c>true</c> if this instance can access the specified member; otherwise, <c>false</c>.</returns>
        /// <param name="Member">The member that might be accessible.</param>
        public bool CanAccess(ITypeMember Member)
        {
            return DeclaringType.CanAccess(Member);
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
                        !p.IsStatic && CanAccess(p))
                    .ToArray();
                instanceIndexerCache[Type] = result;
                return result;
            }
        }

        /// <summary>
        /// Gets all accessible constructors that can be used to
        /// create an instance of the given type.
        /// </summary>
        /// <returns>The list instance constructors.</returns>
        /// <param name="Type">The type to construct.</param>
        public IEnumerable<IMethod> GetInstanceConstructors(IType Type)
        {
            IMethod[] result;
            if (instanceCtorCache.TryGetValue(Type, out result))
            {
                return result;
            }
            else
            {
                result = Type.GetConstructors()
                    .Where(p => 
                        !p.IsStatic && CanAccess(p))
                    .ToArray();
                instanceCtorCache[Type] = result;
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
                return !item.IsStatic && CanAccess(item);
            });
        }

        /// <summary>
        /// Gets all extension methods with the given name that can be accessed
        /// on an instance of the given type. 
        /// </summary>
        public IEnumerable<ITypeMember> GetExtensionMembers(IType Type, string Name)
        {
            return GetMembers(
                Type, Name, extensionMemberCache, 
                Global.ExtensionMemberCache, item =>
            {
                return CanAccess(item);
            });
        }

        /// <summary>
        /// Gets all instance and extension members with the given name that 
        /// can be accessed on an instance of the given type. 
        /// </summary>
        /// <returns>The instance and extension members.</returns>
        /// <param name="Type">The type to access members on.</param>
        /// <param name="Name">The name of the members to access.</param>
        public IEnumerable<ITypeMember> GetInstanceAndExtensionMembers(IType Type, string Name)
        {
            return GetInstanceMembers(Type, Name).Concat(GetExtensionMembers(Type, Name));
        }

        /// <summary>
        /// Gets all members with the given name that can be accessed
        /// on the given type's name.
        /// </summary>
        public IEnumerable<ITypeMember> GetStaticMembers(IType Type, string Name)
        {
            return GetMembers(Type, Name, staticMemberCache, item =>
            {
                return item.IsStatic && CanAccess(item);
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
                    if (CanAccess(item))
                        localOps.Add(item.GetOperator(), item);
                }
                operatorCache[Type] = localOps;
                return localOps.GetAll(Op);
            }
        }

        /// <summary>
        /// Dereferences the given type if it is a pointer type.
        /// Otherwise, the type itself is returned. 
        /// </summary>
        public static IType DereferenceOrId(IType Type)
        {
            return Type != null && Type.GetIsPointer() 
                ? Type.AsPointerType().ElementType
                : Type;
        }

        /// <summary>
        /// Gets the type of a hypothetical 'this' expression for
        /// the given type.
        /// </summary>
        public static IType GetThisExpressionType(IType Type)
        {
            return Type == null
                ? null
                : DereferenceOrId(ThisVariable.GetThisType(Type));
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
            && Warnings.Instance.Shadow.UseWarning(Function.Global.Log.Options))
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
}

