﻿using System;
using System.Collections.Generic;
using System.Linq;
using Loyc.Syntax;
using Loyc;
using Flame.Build;
using Flame.Build.Lazy;
using Flame.Compiler;
using Flame.Compiler.Expressions;
using Flame.Ecs.Diagnostics;
using Flame.Ecs.Semantics;
using Pixie;
using Flame.Ecs.Values;
using EcscMacros;

namespace Flame.Ecs
{
    using GlobalConverter = Func<LNode, IMutableNamespace, GlobalScope, NodeConverter, GlobalScope>;
    using TypeConverter = Func<LNode, GlobalScope, NodeConverter, IType>;
    using LocalTypeConverter = Func<LNode, LocalScope, NodeConverter, IType>;
    using TypeMemberConverter = Func<LNode, LazyDescribedType, GlobalScope, NodeConverter, GlobalScope>;
    using AttributeConverter = Func<LNode, LocalScope, NodeConverter, IEnumerable<IAttribute>>;
    using ExpressionConverter = Func<LNode, LocalScope, NodeConverter, IExpression>;
    using ValueConverter = Func<LNode, LocalScope, NodeConverter, IValue>;
    using TypeOrExpressionConverter = Func<LNode, LocalScope, NodeConverter, TypeOrExpression>;
    using LiteralConverter = Func<object, IExpression>;

    /// <summary>
    /// Defines a type that semantically analyzes a syntax tree by
    /// applying sub-converters to syntax nodes.
    /// </summary>
    public sealed class NodeConverter
    {
        public NodeConverter(
            Func<LNode, ILocalScope, TypeOrExpression> LookupUnqualifiedName,
            ExpressionConverter CallConverter,
            AttributeConverter CustomAttributeConverter)
        {
            this.LookupUnqualifiedName = LookupUnqualifiedName;
            this.CallConverter = CallConverter;
            this.CustomAttributeConverter = CustomAttributeConverter;
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

        public Func<LNode, ILocalScope, TypeOrExpression> LookupUnqualifiedName { get; private set; }

        public ExpressionConverter CallConverter { get; private set; }
        public AttributeConverter CustomAttributeConverter { get; private set; }

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
        /// Converts a global node.
        /// </summary>
        public GlobalScope ConvertGlobal(
            LNode Node, IMutableNamespace Namespace, GlobalScope Scope)
        {
            var conv = GetConverterOrDefault(globalConverters, Node);
            if (conv == null)
            {
                NodeHelpers.LogCannotConvertNode(Node, Scope.Log);
                return Scope;
            }
            else
            {
                return conv(Node, Namespace, Scope, this);
            }
        }

        /// <summary>
        /// Converts a type member node.
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
            LNode Node, GlobalScope Scope, IType DeclaringType)
        {
            return ConvertType(Node, Scope.CreateLocalScope(DeclaringType));
        }

        /// <summary>
        /// Converts the given type reference node.
        /// If the type it describes cannot be resolved
        /// unambiguously, then the error is reported,
        /// and null is returned.
        /// </summary>
        public IType ConvertCheckedType(
            LNode Node, GlobalScope Scope, IType DeclaringType)
        {
            return ConvertCheckedType(Node, Scope.CreateLocalScope(DeclaringType));
        }

        /// <summary>
        /// Converts the given type reference node.
        /// If the type it describes cannot be resolved
        /// unambiguously, then the error is reported,
        /// and the error type is returned.
        /// </summary>
        public IType ConvertCheckedTypeOrError(
            LNode Node, GlobalScope Scope, IType DeclaringType)
        {
            return ConvertCheckedType(Node, Scope, DeclaringType) ?? ErrorType.Instance;
        }

        /// <summary>
        /// Converts the given type reference node.
        /// </summary>
        public IType ConvertType(
            LNode Node, LocalScope Scope)
        {
            return ConvertTypeOrExpression(Node, Scope).CollapseTypes(
                NodeHelpers.ToSourceLocation(Node.Range), Scope.Function.Global);
        }

        /// <summary>
        /// Converts the given type reference node.
        /// If the type it describes cannot be resolved
        /// unambiguously, then the error is reported,
        /// and null is returned.
        /// </summary>
        public IType ConvertCheckedType(
            LNode Node, LocalScope Scope)
        {
            var retType = ConvertType(Node, Scope);
            if (retType == null)
            {
                Scope.Log.LogError(new LogEntry(
                    "type resolution",
                    NodeHelpers.HighlightEven(
                        "cannot resolve type '",
                        NodeHelpers.PrintTypeNode(Node),
                        "'."),
                    NodeHelpers.ToSourceLocation(Node.Range)));
            }
            return retType;
        }

        /// <summary>
        /// Converts the given type reference node.
        /// If the type it describes cannot be resolved
        /// unambiguously, then the error is reported,
        /// and the void type is returned.
        /// </summary>
        public IType ConvertCheckedTypeOrError(
            LNode Node, LocalScope Scope)
        {
            return ConvertCheckedType(Node, Scope) ?? PrimitiveTypes.Void;
        }

        /// <summary>
        /// Converts an attribute node. Null is returned if that fails.
        /// </summary>
        public IEnumerable<IAttribute> ConvertAttribute(
            LNode Node, LocalScope Scope)
        {
            var conv = GetConverterOrDefault(attrConverters, Node);
            if (conv == null)
            {
                if (Node.IsTrivia)
                    return Enumerable.Empty<IAttribute>();
                else
                    return CustomAttributeConverter(Node, Scope, this);
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
            LocalScope Scope)
        {
            foreach (var item in Attributes)
            {
                if (!HandleSpecial(item))
                {
                    var result = ConvertAttribute(item, Scope);
                    foreach (var attr in result)
                        yield return attr;
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
        /// Additionally, access modifiers are treated by a separate
        /// function.
        /// </summary>
        public IEnumerable<IAttribute> ConvertAttributeListWithAccess(
            IEnumerable<LNode> Attributes,
            Func<IEnumerable<LNode>, GlobalScope, AccessModifier> HandleAccess,
            Func<LNode, bool> HandleSpecial, LocalScope Scope)
        {
            var partitioned = NodeHelpers.Partition(Attributes, item => item.IsId && NodeHelpers.IsAccessModifier(item.Name));

            yield return new AccessAttribute(HandleAccess(partitioned.Item1, Scope.Function.Global));
            foreach (var item in ConvertAttributeList(partitioned.Item2, HandleSpecial, Scope))
                yield return item;
        }

        /// <summary>
        /// Converts the given sequence of access modifier nodes
        /// to a single access modifier. A default access modifier
        /// is used if the given sequence of modifiers is empty.
        /// </summary>
        /// <returns>An access modifier.</returns>
        /// <param name="Modifiers">
        /// The sequence of access modifier nodes.
        /// </param>
        /// <param name="Scope">The global scope.</param>
        /// <param name="DefaultAccess">
        /// The default access modifier, which is returned if
        /// the set of access modifier nodes was empty.
        /// </param>
        public AccessModifier ConvertAccessModifiersDefault(
            IEnumerable<LNode> Modifiers, GlobalScope Scope,
            AccessModifier DefaultAccess)
        {
            var accModSet = new HashSet<Symbol>();
            foreach (var item in Modifiers)
            {
                var symbol = item.Name;
                if (!accModSet.Add(symbol) && Scope.UseWarning(EcsWarnings.DuplicateAccessModifierWarning))
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
            if (accMod.HasValue)
            {
                return accMod.Value;
            }
            else
            {
                if (accModSet.Count != 0)
                {
                    // The set of access modifiers we found was no good.
                    // Throw an error in the user's direction.
                    var first = Modifiers.First();
                    var srcLoc = NodeHelpers.ToSourceLocation(first.Range);
                    var fragments = new List<string>();
                    fragments.Add("set of access modifiers '");
                    fragments.Add(first.Name.Name);
                    foreach (var item in Modifiers.Skip(1))
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
                return DefaultAccess;
            }
        }

        /// <summary>
        /// Always returns the 'public' access modifier, and
        /// flags all explicit access modifiers as errors.
        /// </summary>
        /// <returns>The 'public' access modifier.</returns>
        /// <param name="Modifiers">
        /// The sequence of access modifier nodes.
        /// </param>
        /// <param name="Scope">The global scope.</param>
        public AccessModifier ConvertAccessModifiersInterface(
            IEnumerable<LNode> Modifiers, GlobalScope Scope)
        {
            foreach (var item in Modifiers)
            {
                Scope.Log.LogError(new LogEntry(
                    "syntax error",
                    NodeHelpers.HighlightEven(
                        "modifier '", item.Name.Name, "' is not valid on this item, " +
                        "because '", "interface", "' members cannot have access modifiers."),
                    NodeHelpers.ToSourceLocation(item.Range)));
            }
            return AccessModifier.Public;
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
            Func<LNode, bool> HandleSpecial, LocalScope Scope)
        {
            return ConvertAttributeListWithAccess(
                Attributes,
                (nodes, s) =>
                    ConvertAccessModifiersDefault(nodes, s, DefaultAccess),
                HandleSpecial, Scope);
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
            bool IsInterface, Func<LNode, bool> HandleSpecial,
            LocalScope Scope)
        {
            if (IsInterface)
            {
                return ConvertAttributeListWithAccess(
                    Attributes, ConvertAccessModifiersInterface,
                    HandleSpecial, Scope);
            }
            else
            {
                return ConvertAttributeListWithAccess(
                    Attributes, DefaultAccess, HandleSpecial, Scope);
            }
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
                        var result = LookupUnqualifiedName(Node, Scope);
                        return result.WithSourceLocation(
                            NodeHelpers.ToSourceLocation(Node.Range));
                    }
                    else if (Node.IsCall)
                    {
                        return new TypeOrExpression(
                            new ExpressionValue(
                                SourceExpression.Create(
                                    CallConverter(Node, Scope, this),
                                    NodeHelpers.ToSourceLocation(Node.Range))));
                    }
                    else if (Node.IsLiteral)
                    {
                        object val = Node.Value;
                        if (val == null)
                            return new TypeOrExpression(
                                new ExpressionValue(NullExpression.Instance));

                        LiteralConverter litConv;
                        if (literalConverters.TryGetValue(val.GetType(), out litConv))
                        {
                            return new TypeOrExpression(
                                new ExpressionValue(
                                    SourceExpression.Create(
                                        litConv(val),
                                        NodeHelpers.ToSourceLocation(Node.Range))));
                        }
                        else
                        {
                            Scope.Function.Global.Log.LogError(new LogEntry(
                                    "unsupported literal type",
                                    NodeHelpers.HighlightEven(
                                        "literals of type '", val.GetType().FullName, "' are not supported."),
                                    NodeHelpers.ToSourceLocation(Node.Range)));
                            return new TypeOrExpression(
                                new ExpressionValue(ExpressionConverters.ErrorTypeExpression));
                        }
                    }
                }
                NodeHelpers.LogCannotConvertNode(Node, Scope.Function.Global.Log);
                return TypeOrExpression.Empty;
            }
            else
            {
                return conv(Node, Scope, this).WithSourceLocation(
                    NodeHelpers.ToSourceLocation(Node.Range));
            }
        }

        /// <summary>
        /// Converts the given expression node to an IR expression.
        /// </summary>
        public IExpression ConvertExpression(LNode Node, LocalScope Scope)
        {
            return ConvertValue(Node, Scope).CreateGetExpressionOrError(
                Scope, NodeHelpers.ToSourceLocation(Node.Range));
        }

        /// <summary>
        /// Converts the given expression node to a value.
        /// </summary>
        public IValue ConvertValue(LNode Node, LocalScope Scope)
        {
            var result = ConvertTypeOrExpression(Node, Scope);
            if (result.IsExpression)
            {
                return result.Expression;
            }
            else
            {
                return new ErrorValue(new LogEntry(
                    "expression resolution",
                    NodeHelpers.HighlightEven("cannot resolve expression."),
                    NodeHelpers.ToSourceLocation(Node.Range)));
            }
        }

        /// <summary>
        /// Registers a global converter.
        /// </summary>
        public void AddGlobalConverter(Symbol Symbol, GlobalConverter Converter)
        {
            globalConverters[Symbol] = Converter;
        }

        /// <summary>
        /// Registers a type member converter.
        /// </summary>
        public void AddMemberConverter(Symbol Symbol, TypeMemberConverter Converter)
        {
            typeMemberConverters[Symbol] = Converter;
        }

        /// <summary>
        /// Registers a type converter.
        /// </summary>
        public void AddTypeConverter(Symbol Symbol, TypeConverter Converter)
        {
            AddTypeOrExprConverter(Symbol, (node, scope, self) =>
				new TypeOrExpression(new IType[]
                    {
                        Converter(node, scope.Function.Global, self)
                    }));
        }

        /// <summary>
        /// Registers a type converter.
        /// </summary>
        public void AddTypeConverter(Symbol Symbol, LocalTypeConverter Converter)
        {
            AddTypeOrExprConverter(Symbol, (node, scope, self) =>
                new TypeOrExpression(new IType[]
                    {
                        Converter(node, scope, self)
                    }));
        }

        /// <summary>
        /// Registers an attribute converter.
        /// </summary>
        public void AddAttributeConverter(Symbol Symbol, AttributeConverter Converter)
        {
            attrConverters[Symbol] = Converter;
        }

        /// <summary>
        /// Registers a type-or-expression converter.
        /// </summary>
        public void AddTypeOrExprConverter(Symbol Symbol, TypeOrExpressionConverter Converter)
        {
            exprConverters[Symbol] = Converter;
        }

        /// <summary>
        /// Registers an expression converter.
        /// </summary>
        public void AddExprConverter(Symbol Symbol, ExpressionConverter Converter)
        {
            exprConverters[Symbol] = (node, scope, self) => new TypeOrExpression(new ExpressionValue(Converter(node, scope, self)));
        }

        /// <summary>
        /// Registers an expression converter.
        /// </summary>
        public void AddValueConverter(Symbol Symbol, ValueConverter Converter)
        {
            exprConverters[Symbol] = (node, scope, self) => new TypeOrExpression(Converter(node, scope, self));
        }

        /// <summary>
        /// Registers a literal converter.
        /// </summary>
        public void AddLiteralConverter(Type LiteralType, LiteralConverter Converter)
        {
            literalConverters[LiteralType] = Converter;
        }

        /// <summary>
        /// Registers a literal converter.
        /// </summary>
        public void AddLiteralConverter<T>(Func<T, IExpression> Converter)
        {
            AddLiteralConverter(typeof(T), val => Converter((T)val));
        }

        /// <summary>
        /// Maps the given symbol to the given attribute.
        /// </summary>
        public void AliasAttribute(Symbol Symbol, IAttribute Attribute)
        {
            AddAttributeConverter(Symbol, (node, scope, self) => new IAttribute[] { Attribute });
        }

        /// <summary>
        /// Maps the given symbol to the given type.
        /// </summary>
        public void AliasType(Symbol Symbol, IType Type)
        {
            AddTypeConverter(Symbol, (LNode node, GlobalScope scope, NodeConverter self) => Type);
        }

        /// <summary>
        /// Converts an entire compilation unit.
        /// </summary>
        public void ConvertCompilationUnit(
            GlobalScope Scope, IMutableNamespace DeclaringNamespace, IEnumerable<LNode> Nodes)
        {
            var state = Scope;
            foreach (var item in Nodes)
            {
                state = ConvertGlobal(item, DeclaringNamespace, state);
            }
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
                    ExpressionConverters.ConvertCall,
                    AttributeConverters.ConvertCustomAttribute);

                // Global entities
                result.AddGlobalConverter(CodeSymbols.Import, GlobalConverters.ConvertImportDirective);
                result.AddGlobalConverter(CodeSymbols.Alias, GlobalConverters.ConvertAliasDirective);
                result.AddGlobalConverter(CodeSymbols.Namespace, GlobalConverters.ConvertNamespaceDefinition);
                result.AddGlobalConverter(CodeSymbols.Class, GlobalConverters.ConvertClassDefinition);
                result.AddGlobalConverter(CodeSymbols.Struct, GlobalConverters.ConvertStructDefinition);
                result.AddGlobalConverter(CodeSymbols.Interface, GlobalConverters.ConvertInterfaceDefinition);
                result.AddGlobalConverter(CodeSymbols.Enum, GlobalConverters.ConvertEnumDefinition);
                result.AddGlobalConverter(CodeSymbols.Assembly, GlobalConverters.ConvertAssemblyAttribute);

                // Type members
                result.AddMemberConverter(CodeSymbols.Fn, TypeMemberConverters.ConvertFunction);
                result.AddMemberConverter(CodeSymbols.Constructor, TypeMemberConverters.ConvertConstructor);
                result.AddMemberConverter(CodeSymbols.Var, TypeMemberConverters.ConvertField);
                result.AddMemberConverter(CodeSymbols.Property, TypeMemberConverters.ConvertProperty);

                // Attributes
                result.AliasAttribute(CodeSymbols.Abstract, PrimitiveAttributes.Instance.AbstractAttribute);
                result.AliasAttribute(CodeSymbols.Extern, PrimitiveAttributes.Instance.ImportAttribute);
                result.AliasAttribute(CodeSymbols.Virtual, PrimitiveAttributes.Instance.VirtualAttribute);
                result.AddAttributeConverter(EcscSymbols.TriviaDocumentationComment, TriviaConverters.ConvertDocumentationComment);
                // Ignore 'unsafe' attributes for now.
                result.AddAttributeConverter(CodeSymbols.Unsafe, (node, scope, conv) => Enumerable.Empty<IAttribute>());

                // Statements
                result.AddExprConverter(CodeSymbols.Break, ExpressionConverters.ConvertBreakExpression);
                result.AddExprConverter(CodeSymbols.Continue, ExpressionConverters.ConvertContinueExpression);
                result.AddExprConverter(CodeSymbols.Goto, ExpressionConverters.ConvertGotoExpression);
                result.AddExprConverter(CodeSymbols.Label, ExpressionConverters.ConvertLabelExpression);
                result.AddExprConverter(CodeSymbols.For, ExpressionConverters.ConvertForExpression);
                result.AddExprConverter(CodeSymbols.If, ExpressionConverters.ConvertIfExpression);
                result.AddExprConverter(CodeSymbols.While, ExpressionConverters.ConvertWhileExpression);
                result.AddExprConverter(CodeSymbols.DoWhile, ExpressionConverters.ConvertDoWhileExpression);
                result.AddExprConverter(CodeSymbols.Switch, ExpressionConverters.ConvertSwitchExpression);
                result.AddExprConverter(CodeSymbols.Lock, ExpressionConverters.ConvertLockExpression);
                result.AddExprConverter(CodeSymbols.Try, ExpressionConverters.ConvertTryExpression);
                result.AddExprConverter(CodeSymbols.UsingStmt, ExpressionConverters.ConvertUsingExpression);

                // Expressions
                result.AddExprConverter(CodeSymbols.Braces, ExpressionConverters.ConvertBlock);
                result.AddExprConverter(CodeSymbols.Return, ExpressionConverters.ConvertReturn);
                result.AddExprConverter(CodeSymbols.Throw, ExpressionConverters.ConvertThrowExpression);
                result.AddTypeOrExprConverter(CodeSymbols.Dot, ExpressionConverters.ConvertMemberAccess);
                result.AddTypeOrExprConverter(CodeSymbols.Of, ExpressionConverters.ConvertInstantiation);

                // Keyword expressions
                result.AddValueConverter(CodeSymbols.This, ExpressionConverters.ConvertThisExpression);
                result.AddExprConverter(CodeSymbols.Base, ExpressionConverters.ConvertBaseExpression);
                result.AddExprConverter(CodeSymbols.Default, ExpressionConverters.ConvertDefaultExpression);
                result.AddExprConverter(CodeSymbols.New, ExpressionConverters.ConvertNewExpression);
                result.AddExprConverter(CodeSymbols.Sizeof, ExpressionConverters.ConvertSizeofExpression);
                result.AddExprConverter(GSymbol.Get("nameof"), ExpressionConverters.ConvertNameofExpression);

                // Cast expressions
                result.AddExprConverter(CodeSymbols.As, ExpressionConverters.ConvertAsInstanceExpression);
                result.AddExprConverter(CodeSymbols.Is, ExpressionConverters.ConvertIsInstanceExpression);
                result.AddExprConverter(CodeSymbols.Cast, ExpressionConverters.ConvertCastExpression);
                result.AddValueConverter(CodeSymbols.UsingCast, ExpressionConverters.ConvertUsingCastExpression);

                // Variable declaration
                result.AddExprConverter(CodeSymbols.Var, ExpressionConverters.ConvertVariableDeclarationExpression);

                // Ecsc builtins
                result.AliasAttribute(EcscMacros.EcscSymbols.BuiltinHidden, PrimitiveAttributes.Instance.HiddenAttribute);
                result.AddAttributeConverter(EcscSymbols.BuiltinAttribute, AttributeConverters.ConvertBuiltinAttribute);
                result.AddExprConverter(EcscMacros.EcscSymbols.BuiltinStaticIf, ExpressionConverters.ConvertBuiltinStaticIfExpression);
                result.AddExprConverter(EcscMacros.EcscSymbols.BuiltinStaticIsArray, ExpressionConverters.ConvertBuiltinStaticIsArrayExpression);
                result.AddExprConverter(EcscMacros.EcscSymbols.BuiltinRefToPtr, ExpressionConverters.ConvertBuiltinRefToPtrExpression);
                result.AddTypeConverter(EcscMacros.EcscSymbols.BuiltinDecltype, ExpressionConverters.ConvertBuiltinDecltype);
                result.AddTypeConverter(EcscMacros.EcscSymbols.BuiltinDelegateType, ExpressionConverters.ConvertBuiltinDelegateType);
                result.AddTypeOrExprConverter(EcscMacros.EcscSymbols.BuiltinWarningDisable, ExpressionConverters.ConvertBuiltinDisableWarning);
                result.AddTypeOrExprConverter(EcscMacros.EcscSymbols.BuiltinWarningRestore, ExpressionConverters.ConvertBuiltinRestoreWarning);

                // Operators
                // - Ternary operators
                result.AddExprConverter(CodeSymbols.QuestionMark, ExpressionConverters.ConvertSelectExpression);

                // - Binary operators
                result.AddExprConverter(CodeSymbols.Add, UnaryConverters.CreateUnaryOrBinaryOpConverter(Operator.Add));
                result.AddExprConverter(CodeSymbols.Sub, UnaryConverters.CreateUnaryOrBinaryOpConverter(Operator.Subtract));
                result.AddValueConverter(CodeSymbols.Mul, ExpressionConverters.ConvertMultiplyOrDereference);
                result.AddExprConverter(CodeSymbols.Div, ExpressionConverters.CreateBinaryOpConverter(Operator.Divide));
                result.AddExprConverter(CodeSymbols.Mod, ExpressionConverters.CreateBinaryOpConverter(Operator.Remainder));
                result.AddExprConverter(CodeSymbols.Eq, ExpressionConverters.CreateBinaryOpConverter(Operator.CheckEquality));
                result.AddExprConverter(CodeSymbols.Neq, ExpressionConverters.CreateBinaryOpConverter(Operator.CheckInequality));
                result.AddExprConverter(CodeSymbols.LT, ExpressionConverters.CreateBinaryOpConverter(Operator.CheckLessThan));
                result.AddExprConverter(CodeSymbols.LE, ExpressionConverters.CreateBinaryOpConverter(Operator.CheckLessThanOrEqual));
                result.AddExprConverter(CodeSymbols.GT, ExpressionConverters.CreateBinaryOpConverter(Operator.CheckGreaterThan));
                result.AddExprConverter(CodeSymbols.GE, ExpressionConverters.CreateBinaryOpConverter(Operator.CheckGreaterThanOrEqual));
                result.AddExprConverter(CodeSymbols.Shl, ExpressionConverters.CreateBinaryOpConverter(Operator.LeftShift));
                result.AddExprConverter(CodeSymbols.Shr, ExpressionConverters.CreateBinaryOpConverter(Operator.RightShift));
                result.AddExprConverter(CodeSymbols.AndBits, ExpressionConverters.ConvertAndOrAddressOf);
                result.AddExprConverter(CodeSymbols.OrBits, ExpressionConverters.CreateBinaryOpConverter(Operator.Or));
                result.AddExprConverter(CodeSymbols.XorBits, ExpressionConverters.CreateBinaryOpConverter(Operator.Xor));
                result.AddExprConverter(CodeSymbols.And, ExpressionConverters.ConvertLogicalAnd);
                result.AddExprConverter(CodeSymbols.Or, ExpressionConverters.ConvertLogicalOr);

                // - Unary operators
                result.AddExprConverter(CodeSymbols.PreInc, UnaryConverters.ConvertPrefixIncrement);
                result.AddExprConverter(CodeSymbols.PreDec, UnaryConverters.ConvertPrefixDecrement);
                result.AddExprConverter(CodeSymbols.PostInc, UnaryConverters.ConvertPostfixIncrement);
                result.AddExprConverter(CodeSymbols.PostDec, UnaryConverters.ConvertPostfixDecrement);
                result.AddExprConverter(CodeSymbols.NotBits, UnaryConverters.CreateUnaryOpConverter(UnaryOperatorResolution.BitwiseComplement));
                result.AddExprConverter(CodeSymbols.Not, UnaryConverters.CreateUnaryOpConverter(Operator.Not));
                result.AddValueConverter(CodeSymbols.IndexBracks, UnaryConverters.ConvertIndex);

                // - Assignment operator
                result.AddExprConverter(CodeSymbols.Assign, ExpressionConverters.ConvertAssignment);

                // - Compound assignment operators
                result.AddExprConverter(CodeSymbols.AddAssign, ExpressionConverters.CreateCompoundAssignmentConverter(Operator.Add));
                result.AddExprConverter(CodeSymbols.SubAssign, ExpressionConverters.CreateCompoundAssignmentConverter(Operator.Subtract));
                result.AddExprConverter(CodeSymbols.MulAssign, ExpressionConverters.CreateCompoundAssignmentConverter(Operator.Multiply));
                result.AddExprConverter(CodeSymbols.DivAssign, ExpressionConverters.CreateCompoundAssignmentConverter(Operator.Divide));
                result.AddExprConverter(CodeSymbols.ModAssign, ExpressionConverters.CreateCompoundAssignmentConverter(Operator.Remainder));
                result.AddExprConverter(CodeSymbols.ShlAssign, ExpressionConverters.CreateCompoundAssignmentConverter(Operator.LeftShift));
                result.AddExprConverter(CodeSymbols.ShrAssign, ExpressionConverters.CreateCompoundAssignmentConverter(Operator.RightShift));
                result.AddExprConverter(CodeSymbols.AndBitsAssign, ExpressionConverters.CreateCompoundAssignmentConverter(Operator.And));
                result.AddExprConverter(CodeSymbols.OrBitsAssign, ExpressionConverters.CreateCompoundAssignmentConverter(Operator.Or));
                result.AddExprConverter(CodeSymbols.XorBitsAssign, ExpressionConverters.CreateCompoundAssignmentConverter(Operator.Xor));

                // Literals
                result.AddLiteralConverter<sbyte>(val => new IntegerExpression(val));
                result.AddLiteralConverter<short>(val => new IntegerExpression(val));
                result.AddLiteralConverter<int>(val => new IntegerExpression(val));
                result.AddLiteralConverter<long>(val => new IntegerExpression(val));
                result.AddLiteralConverter<byte>(val => new IntegerExpression(val));
                result.AddLiteralConverter<ushort>(val => new IntegerExpression(val));
                result.AddLiteralConverter<uint>(val => new IntegerExpression(val));
                result.AddLiteralConverter<ulong>(val => new IntegerExpression(val));
                result.AddLiteralConverter<float>(val => new Float32Expression(val));
                result.AddLiteralConverter<double>(val => new Float64Expression(val));
                result.AddLiteralConverter<bool>(val => new BooleanExpression(val));
                result.AddLiteralConverter<char>(val => new CharExpression(val));
                result.AddLiteralConverter<string>(val => new StringExpression(val));

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
                result.AddTypeConverter(CodeSymbols.Object, ExpressionConverters.ConvertRootType);

                return result;
            }
        }
    }
}
