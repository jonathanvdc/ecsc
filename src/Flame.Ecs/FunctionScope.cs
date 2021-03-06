﻿using System;
using System.Collections.Generic;
using System.Linq;
using Loyc;
using Flame.Collections;
using Flame.Compiler;
using Flame.Compiler.Expressions;
using Flame.Compiler.Variables;
using Flame.Ecs.Semantics;
using Pixie;
using Flame.Ecs.Diagnostics;
using Flame.Compiler.Statements;

namespace Flame.Ecs
{
    /// <summary>
    /// A data structure that represents the top-level scope of a function-like
    /// member. An enclosing type and a sequence of parameter variables are stored,
    /// but no local variables can be declared in this type of scope.
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
            this.Labels = new FunctionLabelManager();
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
        /// Gets the function label manager for this scope.
        /// </summary>
        /// <returns>The function label manager.</returns>
        public FunctionLabelManager Labels { get; private set; }

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
            if (DeclaringType == null)
            {
                return Member.GetIsGlobalPublic();
            }
            else
            {
                return DeclaringType.CanAccess(Member);
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
        /// Gets all members that can be accessed on an instance of the given type.
        /// </summary>
        public IEnumerable<ITypeMember> GetInstanceMembers(IType Type)
        {
            return Global.MemberCache.GetAllMembers(Type)
                .Where(member => !member.IsStatic && CanAccess(member))
                .ToArray();
        }

        /// <summary>
        /// Gets all extension methods with the given name that can be accessed
        /// on an instance of the given type.
        /// </summary>
        public IEnumerable<ITypeMember> GetExtensionMembers(IType Type, string Name)
        {
            return GetMembers(
                Type, Name, extensionMemberCache,
                Global.ExtensionMemberCache, CanAccess);
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
        /// Gets the set of all instance and extension members that can be
        /// accessed on an instance of the given type.
        /// </summary>
        /// <param name="Type">The type for which members are to be found.</param>
        /// <returns>The set of all instance and extension members
        /// that can be accessed on an instance of the given type.</returns>
        /// <remarks>The use case for this method is to add a "Did you mean ...?"
        /// message to error diagnostics. Don't put calls to this method on
        /// the fast-path.</remarks>
        public IEnumerable<ITypeMember> GetInstanceAndExtensionMembers(IType Type)
        {
            return Global.MemberCache.GetAllMembers(Type)
                .Where(member => !member.IsStatic)
                .Concat(Global.ExtensionMemberCache.GetAllMembers(Type))
                .Where(CanAccess)
                .ToArray();
        }

        /// <summary>
        /// Gets all members with the given name that can be accessed
        /// on the given type.
        /// </summary>
        public IEnumerable<ITypeMember> GetStaticMembers(IType Type, string Name)
        {
            return GetMembers(Type, Name, staticMemberCache, item =>
                {
                    return item.IsStatic && CanAccess(item);
                });
        }

        /// <summary>
        /// Gets the set of all static members that can be accessed
        /// on the given type.
        /// </summary>
        /// <param name="Type">The type for which members are to be found.</param>
        /// <returns>The set of all static members that can be accessed
        /// on the given type.</returns>
        /// <remarks>The use case for this method is to add a "Did you mean ...?"
        /// message to error diagnostics. Don't put calls to this method on
        /// the fast-path.</remarks>
        public IEnumerable<ITypeMember> GetStaticMembers(IType Type)
        {
            return Global.MemberCache.GetAllMembers(Type)
                .Where(member => member.IsStatic && CanAccess(member))
                .ToArray();
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
        /// Gets all static members that can be  found via unqualified name lookup.
        /// </summary>
        /// <remarks>The use case for this method is to add a "Did you mean ...?"
        /// message to error diagnostics. Don't put calls to this method on
        /// the fast-path.</remarks>
        public IEnumerable<ITypeMember> GetUnqualifiedStaticMembers()
        {
            var results = new HashSet<ITypeMember>();
            if (DeclaringType != null)
                results.UnionWith(GetStaticMembers(DeclaringType));

            foreach (var ty in Global.Binder.TypeUsings)
            {
                foreach (var method in GetStaticMembers(ty))
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
                        LogAmbiguousConversion("implicit", From.Type, To, Location);
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
                    new MarkupNode[]
                    {
                        new MarkupNode(NodeConstants.TextNodeType, "cannot implicitly convert type "),
                        RenderConversion(From.Type, To),
                        new MarkupNode(NodeConstants.TextNodeType, "." + (convs.Count > 0
                            ? " An explicit conversion exists. (are you missing a cast?)"
                            : ""))
                    },
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
            var conv = GetImplicitConversion(From, To, Location);
            if (conv.IsStatic
                && EcsWarnings.IntegerDivisionWarning.UseWarning(Global.Log.Options)
                && To.GetIsFloatingPoint())
            {
                // Check for integer division followed by a cast to a floating-point
                // type.
                var divExpr = From.GetEssentialExpression() as DivideExpression;
                if (divExpr != null && divExpr.Type.GetIsInteger())
                {
                    var namer = Global.CreateAbbreviatingRenderer(divExpr.Type, To);
                    Global.Log.LogWarning(new LogEntry(
                        "integer division",
                        EcsWarnings.IntegerDivisionWarning.CreateMessage(new MarkupNode(
                            "message",
                            NodeHelpers.HighlightEven(
                                namer.CreateTextNode("division of '"),
                                namer.Convert(divExpr.Type),
                                namer.CreateTextNode(
                                    "' values followed by an implicit conversion to '"),
                                namer.Convert(To),
                                namer.CreateTextNode(
                                    "' will always truncate the division's result. " +
                                    "Is that what you intended? ")))),
                        Location));
                }
            }
            return ApplyOrUnknown(From, To, conv);
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
                        LogAmbiguousConversion("static", From.Type, To, Location);
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
                    new MarkupNode[]
                    {
                        new MarkupNode(
                            NodeConstants.TextNodeType,
                            "cannot guarantee at compile-time that " +
                            "there is a safe conversion from type "),
                        RenderConversion(From.Type, To),
                        new MarkupNode(NodeConstants.TextNodeType, "." + (convs.Count > 0
                            ? " An explicit conversion exists. (are you missing a cast?)"
                            : ""))
                    },
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
                        LogAmbiguousConversion("explicit", From.Type, To, Location);
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
                    new MarkupNode[]
                    {
                        new MarkupNode(NodeConstants.TextNodeType, "cannot convert type "),
                        RenderConversion(From.Type, To),
                        new MarkupNode(NodeConstants.TextNodeType, "."),
                    },
                    Location));

                return new UnknownExpression(To);
            }
            else
            {
                Global.Log.LogError(new LogEntry(
                    "ambiguous explicit conversion",
                    new MarkupNode[]
                    {
                        new MarkupNode(NodeConstants.TextNodeType, "the explicit conversion of type "),
                        RenderConversion(From.Type, To),
                        new MarkupNode(NodeConstants.TextNodeType,
                            " is ambiguous because " +
                            "no true explicit conversion is applicable, and the implicit " +
                            "conversion is ambiguous."),
                    },
                    Location));
                return ApplyAnyConversion(From, To, convs);
            }
        }

        /// <summary>
        /// Logs an error that states that the conversion of one type to another
        /// is ambiguous.
        /// </summary>
        /// <param name="ConversionType">The type of conversion that is performed.</param>
        /// <param name="FromType">The source type.</param>
        /// <param name="ToType">The target type.</param>
        /// <param name="Location">The source location.</param>
        private void LogAmbiguousConversion(
            string ConversionType, IType FromType, IType ToType, SourceLocation Location)
        {
            Global.Log.LogError(new LogEntry(
                "ambiguous " + ConversionType + " conversion",
                new MarkupNode[]
                {
                    new MarkupNode(NodeConstants.TextNodeType, "the " + ConversionType + " conversion of type "),
                    RenderConversion(FromType, ToType),
                    new MarkupNode(NodeConstants.TextNodeType, " is ambiguous.")
                },
                Location));
        }

        /// <summary>
        /// Renders a conversion as "'FromType' to 'ToType'" where FromType and ToType
        /// are rendered as bright type diffs.
        /// </summary>
        /// <param name="FromType">The type of the value that is converted.</param>
        /// <param name="ToType">The type to which a value is converted.</param>
        /// <returns>A formatted type conversion.</returns>
        public MarkupNode RenderConversion(IType FromType, IType ToType)
        {
            var abbreviatingNamer = Global.CreateAbbreviatingRenderer(FromType, ToType);
            var typeDiffComparer = new TypeDiffComparer(abbreviatingNamer, HasImplicitReferenceConversion);
            var invTypeDiffComparer = new TypeDiffComparer(
                abbreviatingNamer, (a, b) => HasImplicitReferenceConversion(b, a));
            return new MarkupNode(
                "rendered_conversion",
                NodeHelpers.HighlightEven(
                    abbreviatingNamer.CreateTextNode("'"),
                    typeDiffComparer.Compare(ToType, FromType),
                    abbreviatingNamer.CreateTextNode("' to '"),
                    invTypeDiffComparer.Compare(FromType, ToType),
                    abbreviatingNamer.CreateTextNode("'")));
        }

        /// <summary>
        /// Finds out whether a value of the given source type
        /// can be converted implicitly or explicitly to the
        /// given target type.
        /// </summary>
        public bool HasExplicitConversion(IType From, IType To)
        {
            return ClassifyConversion(From, To).Any();
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
        /// an implicit reference conversion.
        /// </summary>
        public bool HasImplicitReferenceConversion(IType From, IType To)
        {
            foreach (var conv in ClassifyConversion(From, To))
            {
                switch (conv.Kind)
                {
                    case ConversionKind.Identity:
                    case ConversionKind.ReinterpretCast:
                        return true;
                    default:
                        break;
                }
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
                switch (conv.Kind)
                {
                    case ConversionKind.DynamicCast:
                    case ConversionKind.Identity:
                    case ConversionKind.ReinterpretCast:
                    case ConversionKind.ImplicitPointerCast:
                    case ConversionKind.ExplicitPointerCast:
                        return true;
                    default:
                        break;
                }
            }
            return false;
        }

        /// <summary>
        /// Finds out whether the given value can be converted
        /// to the given target type by using a reference
        /// conversion.
        /// </summary>
        public bool HasReferenceConversion(IExpression From, IType To)
        {
            foreach (var conv in ClassifyConversion(From, To))
            {
                switch (conv.Kind)
                {
                    case ConversionKind.DynamicCast:
                    case ConversionKind.Identity:
                    case ConversionKind.ReinterpretCast:
                    case ConversionKind.ImplicitPointerCast:
                    case ConversionKind.ExplicitPointerCast:
                        return true;
                    default:
                        break;
                }
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
            result.Labels = Labels;
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

    /// <summary>
    /// Tags and stores all labels within the scope of a function.
    /// </summary>
    public sealed class FunctionLabelManager
    {
        /// <summary>
        /// Creates a new function label manager.
        /// </summary>
        public FunctionLabelManager()
        {
            this.tags = new Dictionary<Symbol, UniqueTag>();
            this.marks = new Dictionary<UniqueTag, SourceLocation>();
            this.gotos = new Dictionary<UniqueTag, List<SourceLocation>>();
        }

        private Dictionary<Symbol, UniqueTag> tags;
        private Dictionary<UniqueTag, SourceLocation> marks;
        private Dictionary<UniqueTag, List<SourceLocation>> gotos;

        private UniqueTag GetOrCreateTag(Symbol Label)
        {
            UniqueTag tag;
            if (!tags.TryGetValue(Label, out tag))
            {
                tag = new UniqueTag(Label.Name);
                tags[Label] = tag;
                gotos[tag] = new List<SourceLocation>();
            }
            return tag;
        }

        /// <summary>
        /// Creates a statement that branches to the given label.
        /// </summary>
        /// <param name="Label">The label to branch to.</param>
        /// <param name="Location">The source location of the goto.</param>
        /// <returns>A goto statement.</returns>
        public IStatement CreateGotoStatement(Symbol Label, SourceLocation Location)
        {
            var tag = GetOrCreateTag(Label);
            gotos[tag].Add(Location);
            return new GotoLabelStatement(tag);
        }

        /// <summary>
        /// Creates a statement that marks the given label.
        /// </summary>
        /// <param name="Label">The label to mark.</param>
        /// <param name="Location">The source location of the label.</param>
        /// <param name="Log">A log to send errors to.</param>
        /// <returns>A mark statement.</returns>
        public IStatement CreateMarkStatement(Symbol Label, SourceLocation Location, ICompilerLog Log)
        {
            var tag = GetOrCreateTag(Label);
            SourceLocation existingMark;
            if (marks.TryGetValue(tag, out existingMark))
            {
                Log.LogError(new LogEntry(
                    "label redefinition",
                    NodeHelpers.CreateRedefinitionMessage(
                        NodeHelpers.HighlightEven(
                            "label '", Label.Name, "' is defined more than once."),
                        Location,
                        existingMark)));
            }
            marks[tag] = Location;
            return new MarkLabelStatement(tag);
        }

        /// <summary>
        /// Issues diagnostics that clarify that the given label
        /// has been referenced but was never marked.
        /// </summary>
        /// <param name="Tag">The tag of the block that was not marked.</param>
        /// <param name="Gotos">A list of gotos that reference the label.</param>
        /// <param name="Log">A log to send errors to.</param>
        /// <param name="AllLabels">A sequence of all labels in the function.</param>
        private static void ReportNotMarked(
            UniqueTag Tag,
            List<SourceLocation> Gotos,
            ICompilerLog Log,
            IEnumerable<string> AllLabels)
        {
            string suggestedName = NameSuggestionHelpers.SuggestName(Tag.Name, AllLabels);
            var message = suggestedName == null
                ? NodeHelpers.HighlightEven(
                    "", "goto", " statement targets undefined label '",
                    Tag.Name, "'.")
                : NodeHelpers.HighlightEven(
                    "", "goto", " statement targets undefined label '",
                    Tag.Name, "'. Did you mean '", suggestedName, "'?");

            foreach (var location in Gotos)
            {
                Log.LogError(
                    new LogEntry(
                        "undefined label",
                        message,
                        location));
            }
        }

        /// <summary>
        /// Checks that all labels referenced by goto statements have been marked
        /// by mark statements.
        /// </summary>
        /// <param name="Log">A log to send errors to.</param>
        /// <returns>
        /// <c>true</c> if all referenced labels have been marked; otherwise, <c>false</c>.
        /// </returns>
        public bool CheckAllMarked(ICompilerLog Log)
        {
            IEnumerable<string> allLabels = null;
            foreach (var pair in gotos)
            {
                if (!marks.ContainsKey(pair.Key))
                {
                    if (allLabels == null)
                    {
                        allLabels = MarkedLabelsAsStrings;
                    }
                    ReportNotMarked(pair.Key, pair.Value, Log, allLabels);
                }
            }
            return allLabels == null;
        }

        private IEnumerable<string> MarkedLabelsAsStrings
        {
            get
            {
                var results = new HashSet<string>();
                foreach (var pair in marks)
                {
                    results.Add(pair.Key.Name);
                }
                return results;
            }
        }

        /// <summary>
        /// Checks that all labels defined by mark statements that have been
        /// used by goto statements.
        /// </summary>
        /// <param name="Log">A log to send warnings to.</param>
        /// <returns>
        /// <c>true</c> if all defined labels have been used; otherwise, <c>false</c>.
        /// </returns>
        public bool CheckAllUsed(ICompilerLog Log)
        {
            bool allUsed = true;
            foreach (var pair in gotos)
            {
                SourceLocation markLocation;
                if (pair.Value.Count == 0 && marks.TryGetValue(pair.Key, out markLocation))
                {
                    Log.LogWarning(
                        new LogEntry(
                            "unused label",
                            NodeHelpers.HighlightEven(
                                "label '", pair.Key.Name, "' is defined but never used."),
                            markLocation));
                    allUsed = false;
                }
            }
            return allUsed;
        }
    }
}

