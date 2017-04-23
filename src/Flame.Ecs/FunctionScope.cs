using System;
using System.Collections.Generic;
using System.Linq;
using Loyc;
using Flame.Collections;
using Flame.Compiler;
using Flame.Compiler.Expressions;
using Flame.Compiler.Variables;
using Flame.Ecs.Semantics;

namespace Flame.Ecs
{
    /// <summary>
    /// A data structure that represents the top-level scope
    /// of a function-like member. An enclosing type and
    /// a sequence of parameter variables are stored, but
    /// no local variables can be declared in this type of scope. 
    /// </summary>
    public sealed class FunctionScope : ILocalScope
    {
        private FunctionScope()
        {

        }

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
            this.conversionCache = new Dictionary<KeyValuePair<IType, IType>, IReadOnlyList<ConversionDescription>>();

            this.Global = Global;
            this.Flow = new LocalFlow();
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
        public LocalFlow Flow { get; private set; }

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
        private Dictionary<KeyValuePair<IType, IType>, IReadOnlyList<ConversionDescription>> conversionCache;

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

        /// <summary>
        /// Classifies a conversion of the given expression to the given type,
        /// within the context of the current function scope.
        /// </summary>
        public IReadOnlyList<ConversionDescription> ClassifyConversion(
            IExpression From, IType To)
        {
            return Global.ConversionRules.ClassifyConversion(From, To, this);
        }

        /// <summary>
        /// Classifies a conversion of the given expression to the given type,
        /// within the context of the current function scope.
        /// </summary>s
        public IReadOnlyList<ConversionDescription> ClassifyConversion(
            IType From, IType To)
        {
            var kvPair = new KeyValuePair<IType, IType>(From, To);
            IReadOnlyList<ConversionDescription> result;
            if (!conversionCache.TryGetValue(kvPair, out result))
            {
                result = Global.ConversionRules.ClassifyConversion(From, To, this);
                conversionCache[kvPair] = result;
            }
            return result;
        }

        private ConversionDescription PickAnyConversion(
            IReadOnlyList<ConversionDescription> Conversions)
        {
            if (Conversions.Count > 0)
                return Conversions[0];
            else
                return ConversionDescription.None;
        }

        private IExpression ApplyOrUnknown(
            IExpression From, IType To,
            ConversionDescription Conversion)
        {
            if (Conversion.Kind != ConversionKind.None)
                return Conversion.Convert(From, To);
            else
                return new UnknownExpression(To);
        }

        private IExpression ApplyAnyConversion(
            IExpression From, IType To,
            IReadOnlyList<ConversionDescription> Conversions)
        {
            return ApplyOrUnknown(From, To, PickAnyConversion(Conversions));
        }

        /// <summary>
        /// Gets an implicit conversion of the given expression to the given type.
        /// A diagnostic is issued if this is not a legal operation,
        /// but the resulting expression is always of the given target type,
        /// and is never null.
        /// </summary>
        public ConversionDescription GetImplicitConversion(
            IExpression From, IType To, SourceLocation Location)
        {
            var convs = ClassifyConversion(From, To);

            var result = ConversionDescription.None;
            foreach (var item in convs)
            {
                if (item.IsImplicit)
                {
                    if (result.Kind != ConversionKind.None)
                    {
                        Global.Log.LogError(new LogEntry(
                            "ambiguous implicit conversion",
                            NodeHelpers.HighlightEven(
                                "the implicit conversion of type '", Global.TypeNamer.Convert(From.Type),
                                "' to '", Global.TypeNamer.Convert(To), "' is ambiguous."),
                            Location));
                        return result;
                    }
                    else
                    {
                        result = item;
                    }
                }
            }

            if (result.Kind == ConversionKind.None)
            {
                Global.Log.LogError(new LogEntry(
                    "no implicit conversion",
                    NodeHelpers.HighlightEven(
                        "cannot implicitly convert type '", Global.TypeNamer.Convert(From.Type),
                        "' to '", Global.TypeNamer.Convert(To), "'." +
                        (convs.Count > 0 ? " An explicit conversion exists. (are you missing a cast?)" : "")),
                    Location));

                return PickAnyConversion(convs);
            }
            else
            {
                return result;
            }
        }

        /// <summary>
        /// Implicitly converts the given expression to the given type.
        /// A diagnostic is issued if this is not a legal operation,
        /// but the resulting expression is always of the given target type,
        /// and is never null.
        /// </summary>
        public IExpression ConvertImplicit(IExpression From, IType To, SourceLocation Location)
        {
            return ApplyOrUnknown(From, To, GetImplicitConversion(From, To, Location));
        }

        /// <summary>
        /// Statically converts the given expression to the given type.
        /// A diagnostic is issued if this is not a legal operation,
        /// but the resulting expression is always of the given target type,
        /// and is never null.
        /// </summary>
        public IExpression ConvertStatic(IExpression From, IType To, SourceLocation Location)
        {
            var convs = ClassifyConversion(From, To);

            IExpression result = null;
            foreach (var item in convs)
            {
                if (item.IsStatic)
                {
                    if (result != null)
                    {
                        Global.Log.LogError(new LogEntry(
                            "ambiguous static conversion",
                            NodeHelpers.HighlightEven(
                                "the static conversion of type '", Global.TypeNamer.Convert(From.Type),
                                "' to '", Global.TypeNamer.Convert(To), "' is ambiguous."),
                            Location));
                        return result;
                    }
                    else
                    {
                        result = item.Convert(From, To);
                    }
                }
            }

            if (result == null)
            {
                Global.Log.LogError(new LogEntry(
                    "no static conversion",
                    NodeHelpers.HighlightEven(
                        "cannot guarantee at compile-time that type '", Global.TypeNamer.Convert(From.Type),
                        "' can safely be converted to '", Global.TypeNamer.Convert(To), "'." +
                        (convs.Count > 0 ? " An explicit conversion exists." : "")),
                    Location));

                return ApplyAnyConversion(From, To, convs);
            }
            else
            {
                return result;
            }
        }

        /// <summary>
        /// Explicitly converts the given expression to the given type.
        /// A diagnostic is issued if this is not a legal operation,
        /// but the resulting expression is always of the given target type,
        /// and is never null.
        /// </summary>
        public IExpression ConvertExplicit(IExpression From, IType To, SourceLocation Location)
        {
            var convs = ClassifyConversion(From, To);

            if (convs.Count == 1)
            {
                // This is the usual fast-path.
                return convs[0].Convert(From, To);
            }

            // Prefer explicit conversions over implicit conversions
            // here.
            IExpression result = null;
            foreach (var item in convs)
            {
                if (item.IsExplicit)
                {
                    if (result != null)
                    {
                        Global.Log.LogError(new LogEntry(
                            "ambiguous explicit conversion",
                            NodeHelpers.HighlightEven(
                                "the explicit conversion of type '", Global.TypeNamer.Convert(From.Type),
                                "' to '", Global.TypeNamer.Convert(To), "' is ambiguous."),
                            Location));
                        return result;
                    }
                    else
                    {
                        result = item.Convert(From, To);
                    }
                }
            }

            if (convs.Count == 0)
            {
                Global.Log.LogError(new LogEntry(
                    "no conversion",
                    NodeHelpers.HighlightEven(
                        "cannot convert type '", Global.TypeNamer.Convert(From.Type),
                        "' to '", Global.TypeNamer.Convert(To), "'."),
                    Location));

                return new UnknownExpression(To);
            }
            else
            {
                Global.Log.LogError(new LogEntry(
                    "ambiguous explicit conversion",
                    NodeHelpers.HighlightEven(
                        "the explicit conversion of type '", Global.TypeNamer.Convert(From.Type),
                        "' to '", Global.TypeNamer.Convert(To), "' is ambiguous, because " +
                        "there no true explicit conversion was applicable, and the implicit " +
                        "conversion was ambiguous."),
                    Location));
                return ApplyAnyConversion(From, To, convs);
            }
        }

        /// <summary>
        /// Finds out whether a value of the given source type
        /// can be converted implicitly to the given target type.
        /// </summary>
        public bool HasImplicitConversion(IType From, IType To)
        {
            foreach (var conv in ClassifyConversion(From, To))
            {
                if (conv.IsImplicit)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Finds out whether the given value
        /// can be converted implicitly to the given target type.
        /// </summary>
        public bool HasImplicitConversion(IExpression From, IType To)
        {
            foreach (var conv in ClassifyConversion(From, To))
            {
                if (conv.IsImplicit)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Finds out whether a value of the given source type
        /// can be converted to the given target type by using
        /// a reference conversion.
        /// </summary>
        public bool HasReferenceConversion(IType From, IType To)
        {
            foreach (var conv in ClassifyConversion(From, To))
            {
                if (conv.Kind == ConversionKind.DynamicCast)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Finds out whether the given value
        /// can be converted to the given target type by using
        /// a reference conversion.
        /// </summary>
        public bool HasReferenceConversion(IExpression From, IType To)
        {
            foreach (var conv in ClassifyConversion(From, To))
            {
                if (conv.Kind == ConversionKind.DynamicCast)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Creates a copy of this function scope.
        /// </summary>
        /// <returns>A copy of this function scope.</returns>
        private FunctionScope Copy()
        {
            var result = new FunctionScope();
            result.instanceMemberCache = instanceMemberCache;
            result.extensionMemberCache = extensionMemberCache;
            result.staticMemberCache = staticMemberCache;
            result.instanceCtorCache = instanceCtorCache;
            result.instanceIndexerCache = instanceIndexerCache;
            result.operatorCache = operatorCache;
            result.conversionCache = conversionCache;

            result.Global = Global;
            result.CurrentType = CurrentType;
            result.DeclaringType = DeclaringType;
            result.CurrentMethod = CurrentMethod;
            result.ReturnType = ReturnType;
            result.ParameterVariables = ParameterVariables;
            return result;
        }

        /// <summary>
        /// Creates a copy of this function scope and assigns it the given global scope.
        /// </summary>
        /// <param name="NewGlobalScope">The new global scope.</param>
        /// <returns>The new function scope.</returns>
        private FunctionScope WithGlobalScope(GlobalScope NewGlobalScope)
        {
            var result = Copy();
            result.Global = NewGlobalScope;
            return result;
        }

        /// <summary>
        /// Creates a new function scope that disables the warnings with the given names.
        /// </summary>
        /// <param name="WarningNames">The names of the warnings to disables.</param>
        /// <returns>The new function scope.</returns>
        public FunctionScope DisableWarnings(params string[] WarningNames)
        {
            return WithGlobalScope(Global.DisableWarnings(WarningNames));
        }

        /// <summary>
        /// Creates a new function scope that restores the warnings with the given names.
        /// </summary>
        /// <param name="WarningNames">The names of the warnings to restore.</param>
        /// <returns>The new function scope.</returns>
        public FunctionScope RestoreWarnings(params string[] WarningNames)
        {
            return WithGlobalScope(Global.RestoreWarnings(WarningNames));
        }
    }
}

