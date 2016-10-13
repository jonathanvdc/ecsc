using System;
using System.Linq;
using Loyc.Syntax;
using Flame.Build;
using Flame.Build.Lazy;
using Flame.Compiler;
using Flame.Compiler.Statements;
using System.Collections.Generic;
using Flame.Compiler.Variables;
using Pixie;
using Flame.Ecs.Semantics;

namespace Flame.Ecs
{
    public static class TypeMemberConverters
    {
        /// <summary>
        /// Converts a parameter declaration node.
        /// </summary>
        public static IParameter ConvertParameter(LNode Node, GlobalScope Scope, NodeConverter Converter)
        {
            var name = NodeHelpers.ToUnqualifiedName(Node.Args[1], Scope);
            var paramTy = Converter.ConvertType(Node.Args[0], Scope);
            if (paramTy == null)
            {
                Scope.Log.LogError(new LogEntry(
                    "type resolution",
                    NodeHelpers.HighlightEven(
                        "could not resolve parameter type '", Node.Args[0].ToString(), 
                        "' for parameter '", name.Item1.ToString(), "'."),
                    NodeHelpers.ToSourceLocation(Node.Args[0].Range)));
                paramTy = PrimitiveTypes.Void;
            }
            bool isOut = false;
            var attrs = Converter.ConvertAttributeList(Node.Attrs, node =>
            {
                if (node.IsIdNamed(CodeSymbols.Ref))
                {
                    paramTy = paramTy.MakePointerType(PointerKind.ReferencePointer);
                    return true;
                }
                else if (node.IsIdNamed(CodeSymbols.Out))
                {
                    paramTy = paramTy.MakePointerType(PointerKind.ReferencePointer);
                    isOut = true;
                    return true;
                }
                else
                {
                    return false;
                }
            }, Scope).ToArray();
            var descParam = new DescribedParameter(name.Item1, paramTy);
            foreach (var item in attrs)
            {
                descParam.AddAttribute(item);
            }
            if (isOut)
            {
                descParam.AddAttribute(PrimitiveAttributes.Instance.OutAttribute);
            }
            return descParam;
        }

        /// <summary>
        /// Analyzes an attribute list that belongs to a type 
        /// member. A sequence of arguments and a boolean that
        /// specifies static-ness are returned.
        /// </summary>
        private static Tuple<IEnumerable<IAttribute>, bool> AnalyzeTypeMemberAttributes(
            IEnumerable<LNode> Attributes, IType DeclaringType,
            GlobalScope Scope, NodeConverter Converter,
            Func<LNode, bool> HandleSpecial)
        {
            bool isStatic = false;
            bool isInterface = DeclaringType.GetIsInterface();
            var attrs = Converter.ConvertAttributeListWithAccess(
                            Attributes, isInterface ? AccessModifier.Public : AccessModifier.Private,
                            isInterface, node =>
                {
                    if (node.IsIdNamed(CodeSymbols.Static))
                    {
                        if (isInterface)
                        {
                            // Interfaces can't contain 'static' members. 
                            // Report an error.
                            Scope.Log.LogError(new LogEntry(
                                "syntax error",
                                NodeHelpers.HighlightEven(
                                    "'", "interface", "' members cannot be '", "static", "'."),
                                NodeHelpers.ToSourceLocation(node.Range)));
                        }
                        isStatic = true;
                        return true;
                    }
                    else
                    {
                        return HandleSpecial(node);
                    }
                }, Scope);
            return Tuple.Create<IEnumerable<IAttribute>, bool>(
                attrs.ToArray(), isStatic);
        }

        /// <summary>
        /// Analyzes an attribute list that belongs to a type 
        /// member. A sequence of arguments and a boolean that
        /// specifies static-ness are returned.
        /// </summary>
        private static Tuple<IEnumerable<IAttribute>, bool> AnalyzeTypeMemberAttributes(
            IEnumerable<LNode> Attributes, IType DeclaringType,
            GlobalScope Scope, NodeConverter Converter)
        {
            return AnalyzeTypeMemberAttributes(
                Attributes, DeclaringType, Scope, 
                Converter, _ => false);
        }

        /// <summary>
        /// Updates the given type member's attribute list
        /// with the attributes defined by the given 
        /// type member attribute tuple.
        /// </summary>
        private static void UpdateTypeMemberAttributes(
            Tuple<IEnumerable<IAttribute>, bool> Attributes, 
            LazyDescribedTypeMember Target)
        {
            Target.IsStatic = Attributes.Item2;
            Target.AddAttributes(Attributes.Item1);
        }

        /// <summary>
        /// Analyzes the given type member's attribute list,
        /// and updates said list right away.
        /// </summary>
        private static void UpdateTypeMemberAttributes(
            IEnumerable<LNode> Attributes, LazyDescribedTypeMember Target,
            GlobalScope Scope, NodeConverter Converter,
            Func<LNode, bool> HandleSpecial)
        {
            UpdateTypeMemberAttributes(
                AnalyzeTypeMemberAttributes(
                    Attributes, Target.DeclaringType, 
                    Scope, Converter, HandleSpecial),
                Target);
        }

        /// <summary>
        /// Analyzes the given type member's attribute list,
        /// and updates said list right away.
        /// </summary>
        private static void UpdateTypeMemberAttributes(
            IEnumerable<LNode> Attributes, LazyDescribedTypeMember Target,
            GlobalScope Scope, NodeConverter Converter)
        {
            UpdateTypeMemberAttributes(
                AnalyzeTypeMemberAttributes(
                    Attributes, Target.DeclaringType, Scope, Converter),
                Target);
        }

        /// <summary>
        /// Creates a variable that represents the given parameter.
        /// </summary>
        private static IVariable CreateArgumentVariable(IParameter Parameter, int Index)
        {
            var argVar = new ArgumentVariable(Parameter, Index);
            var ptrVarType = Parameter.ParameterType.AsPointerType();
            if (ptrVarType != null && ptrVarType.PointerKind.Equals(PointerKind.ReferencePointer))
            {
                return new AtAddressVariable(argVar.CreateGetExpression());
            }
            else
            {
                return argVar;
            }
        }

        /// <summary>
        /// Analyzes the given parameter list for the
        /// given described method.
        /// </summary>
        private static void AnalyzeParameters(
            IEnumerable<LNode> Parameters, LazyDescribedMethod Target,
            GlobalScope Scope, NodeConverter Converter)
        {
            foreach (var item in Parameters)
            {
                Target.AddParameter(ConvertParameter(item, Scope, Converter));
            }
        }

        /// <summary>
        /// Creates a function scope for the given method.
        /// </summary>
        public static FunctionScope CreateFunctionScope(
            IMethod Method, GlobalScope Scope)
        {
            var thisTy = ThisVariable.GetThisType(Method.DeclaringType);
            var paramVarDict = new Dictionary<string, IVariable>();
            if (!Method.IsStatic)
            {
                paramVarDict[CodeSymbols.This.Name] = ThisReferenceVariable.Instance.Create(thisTy);
            }
            int paramIndex = 0;
            foreach (var parameter in Method.Parameters)
            {
                paramVarDict[parameter.Name.ToString()] = CreateArgumentVariable(parameter, paramIndex);
                paramIndex++;
            }
            return new FunctionScope(Scope, thisTy, Method, Method.ReturnType, paramVarDict);
        }

        public static GlobalScope CreateGenericScope(
            IGenericMember Member, GlobalScope Scope)
        {
            var innerScope = Scope;
            foreach (var item in Member.GenericParameters)
            {
                // Create generic parameters.
                innerScope = innerScope.WithBinder(
                    innerScope.Binder.AliasType(item.Name, item));
            }
            return innerScope;
        }

        /// <summary>
        /// Creates a function scope for the given type member,
        /// which does not take any parameters.
        /// </summary>
        public static FunctionScope CreateTypeMemberScope(
            ITypeMember Member, IType ReturnType, GlobalScope Scope)
        {
            return new FunctionScope(
                Scope, ThisVariable.GetThisType(Member.DeclaringType), 
                null, ReturnType, new Dictionary<string, IVariable>());
        }

        private static void LogStaticVirtualMethodError(
            string MemberKind, ITypeMember Member, string VirtualAttribute, 
            SourceLocation Location, ICompilerLog Log)
        {
            Log.LogError(new LogEntry(
                "syntax error",
                NodeHelpers.HighlightEven(
                    "'", "static", "' " + MemberKind + " '", Member.Name.ToString(), 
                    "' cannot be marked '", VirtualAttribute, "'."),
                Location));
        }

        private static void CheckNonInterfaceMemberAttribute(
            LNode Node, ITypeMember Target, GlobalScope Scope)
        {
            if (Target.DeclaringType.GetIsInterface())
            {
                Scope.Log.LogError(new LogEntry(
                    "syntax error",
                    NodeHelpers.HighlightEven(
                        "modifier '", Node.Name.Name, "' cannot be applied to '", 
                        "interface", "' members."),
                    NodeHelpers.ToSourceLocation(Node.Range)));
            }
        }

        /// <summary>
        /// Handles type member attributes for members that
        /// can support abstract, virtual, sealed, override and
        /// new attributes.
        /// </summary>
        /// <returns>An override-node, new-node pair.</returns>
        private static Tuple<LNode, LNode> UpdateVirtualTypeMemberAttributes(
            string MemberKind, IEnumerable<LNode> Attributes, 
            LazyDescribedTypeMember Target,
            GlobalScope Scope, NodeConverter Converter)
        {
            LNode overrideNode = null;
            LNode abstractNode = null;
            LNode virtualNode = null;
            LNode sealedNode = null;
            LNode newNode = null;
            UpdateTypeMemberAttributes(Attributes, Target, Scope, Converter, node =>
            {
                if (node.IsIdNamed(CodeSymbols.Override))
                {
                    overrideNode = node;
                    CheckNonInterfaceMemberAttribute(node, Target, Scope);
                    return true;
                }
                else if (node.IsIdNamed(CodeSymbols.Abstract))
                {
                    abstractNode = node;
                    CheckNonInterfaceMemberAttribute(node, Target, Scope);
                    return true;
                }
                else if (node.IsIdNamed(CodeSymbols.Virtual))
                {
                    virtualNode = node;
                    CheckNonInterfaceMemberAttribute(node, Target, Scope);
                    return true;
                }
                else if (node.IsIdNamed(CodeSymbols.Sealed))
                {
                    sealedNode = node;
                    CheckNonInterfaceMemberAttribute(node, Target, Scope);
                    return true;
                }
                else if (node.IsIdNamed(CodeSymbols.New))
                {
                    newNode = node;
                    CheckNonInterfaceMemberAttribute(node, Target, Scope);
                    return true;
                }
                else
                {
                    return false;
                }
            });

            bool isVirtual = abstractNode != null || virtualNode != null
                             || (overrideNode != null && sealedNode == null);

            // Verify that these attributes make sense.
            if ((abstractNode != null || virtualNode != null) && sealedNode != null)
            {
                Scope.Log.LogError(new LogEntry(
                    "syntax error",
                    NodeHelpers.HighlightEven(
                        MemberKind + " '", Target.Name.ToString(), "' cannot be marked both '", 
                        (abstractNode != null ? "abstract" : "virtual"), 
                        "' and '", "sealed", "'."),
                    NodeHelpers.ToSourceLocation(sealedNode.Range)));
            }

            if (newNode != null && overrideNode != null)
            {
                Scope.Log.LogError(new LogEntry(
                    "syntax error",
                    NodeHelpers.HighlightEven(
                        MemberKind + " '", Target.Name.ToString(), "' cannot be marked both '", 
                        "new", "' and '", "override", "'."),
                    NodeHelpers.ToSourceLocation(newNode.Range)));
            }

            if (overrideNode == null && sealedNode != null)
            {
                Scope.Log.LogError(new LogEntry(
                    "syntax error",
                    NodeHelpers.HighlightEven(
                        MemberKind + " '", Target.Name.ToString(), "' cannot be '", 
                        "sealed", "' because it is not an '", 
                        "override", "'."),
                    NodeHelpers.ToSourceLocation(overrideNode.Range)));
            }

            if (Target.IsStatic)
            {
                // Static methods can't be abstract, virtual or override.
                if (abstractNode != null)
                    LogStaticVirtualMethodError(
                        MemberKind, Target, "abstract", 
                        NodeHelpers.ToSourceLocation(abstractNode.Range), 
                        Scope.Log);
                if (virtualNode != null)
                    LogStaticVirtualMethodError(
                        MemberKind, Target, "virtual", 
                        NodeHelpers.ToSourceLocation(virtualNode.Range), 
                        Scope.Log);
                if (overrideNode != null)
                    LogStaticVirtualMethodError(
                        MemberKind, Target, "override", 
                        NodeHelpers.ToSourceLocation(overrideNode.Range), 
                        Scope.Log);
                if (newNode != null)
                    LogStaticVirtualMethodError(
                        MemberKind, Target, "new", 
                        NodeHelpers.ToSourceLocation(newNode.Range), 
                        Scope.Log);
            }

            if (abstractNode != null)
            {
                Target.AddAttribute(PrimitiveAttributes.Instance.AbstractAttribute);
                if (!Target.DeclaringType.GetIsAbstract())
                {
                    Scope.Log.LogError(new LogEntry(
                        "syntax error",
                        NodeHelpers.HighlightEven(
                            MemberKind + " '", Target.Name.ToString(), "' cannot be '", 
                            "abstract", "' because its declaring type is not '", 
                            "abstract", "', either."),
                        NodeHelpers.ToSourceLocation(overrideNode.Range)));
                }
            }

            if (isVirtual)
                Target.AddAttribute(PrimitiveAttributes.Instance.VirtualAttribute);

            return Tuple.Create(overrideNode, newNode);
        }

        private static Operator ParseOperatorName(string Name)
        {
            if (Name == UnaryOperatorResolution.BitwiseComplement.Name)
                return UnaryOperatorResolution.BitwiseComplement;
            else
                return Operator.GetOperator(Name);
        }

        /// <summary>
        /// Converts an '#fn' function declaration node.
        /// </summary>
        public static GlobalScope ConvertFunction(
            LNode Node, LazyDescribedType DeclaringType, 
            GlobalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckMinArity(Node, 3, Scope.Log)
                || !NodeHelpers.CheckMaxArity(Node, 4, Scope.Log))
                return Scope;

            // Handle the function's name first.
            var name = NodeHelpers.ToUnqualifiedName(Node.Args[1], Scope);
            var op = ParseOperatorName(name.Item1.Name);
            Tuple<LNode, LNode> attrNodePair = null;
            var def = new LazyDescribedMethod(new SimpleName(name.Item1.Name), DeclaringType, methodDef =>
            {
                // Take care of the generic parameters next.
                var innerScope = Scope;
                foreach (var item in name.Item2(methodDef))
                {
                    // Create generic parameters.
                    methodDef.AddGenericParameter(item);
                    innerScope = innerScope.WithBinder(innerScope.Binder.AliasType(item.Name, item));
                }

                // Attributes next. override, abstract, virtual, sealed
                // and new are specific to methods, so we'll handle those here.
                attrNodePair = UpdateVirtualTypeMemberAttributes("method", Node.Attrs, methodDef, innerScope, Converter);
                methodDef.AddAttribute(new SourceLocationAttribute(NodeHelpers.ToSourceLocation(Node.Args[1].Range)));

                if (op.IsDefined)
                {
                    if (!methodDef.IsStatic || methodDef.GetAccess() != AccessModifier.Public)
                    {
                        Scope.Log.LogError(new LogEntry(
                            "syntax error",
                            NodeHelpers.HighlightEven(
                                "user-defined operator '", name.Item1.Name, "' must be declared '", 
                                "static", "' and '", "public", "'."),
                            methodDef.GetSourceLocation()));
                    }

                    methodDef.AddAttribute(new OperatorAttribute(op));
                }

                // Resolve the return type.
                var retType = Converter.ConvertType(Node.Args[0], innerScope);
                if (retType == null)
                {
                    Scope.Log.LogError(new LogEntry(
                        "type resolution",
                        NodeHelpers.HighlightEven(
                            "could not resolve return type '", 
                            Node.Args[0].ToString(), "' for method '", 
                            name.Item1.ToString(), "'."),
                        NodeHelpers.ToSourceLocation(Node.Args[0].Range)));
                    retType = PrimitiveTypes.Void;
                }
                methodDef.ReturnType = retType;

                // Resolve the parameters
                AnalyzeParameters(
                       Node.Args[2].Args, methodDef, innerScope, Converter);

                // Operator methods must take at least one parameter of the 
                // declaring type.
                if (op.IsDefined)
                {
                    if (!methodDef.Parameters.Any(p => 
                        p.ParameterType.Equals(
                            FunctionScope.GetThisExpressionType(
                                methodDef.DeclaringType))))
                    {
                        Scope.Log.LogError(new LogEntry(
                            "user-defined operator",
                            NodeHelpers.HighlightEven(
                                "at least one of the parameters of user-defined operator '", 
                                name.Item1.Name,
                                "' must be the containing type."),
                            methodDef.GetSourceLocation()));
                    }
                }
            }, methodDef =>
            {
                // Handle overrides
                if (!methodDef.IsStatic)
                {
                    var innerScope = CreateGenericScope(methodDef, Scope);
                    var funScope = CreateFunctionScope(methodDef, innerScope);
                    var overrideNode = attrNodePair.Item1;
                    var newNode = attrNodePair.Item2;

                    var parentTy = DeclaringType.GetParent();
                    if (parentTy != null)
                    {
                        var baseMethods = funScope.GetInstanceMembers(parentTy, name.Item1.Name)
                            .OfType<IMethod>()
                            .Where(m => m.HasSameCallSignature(methodDef))
                            .ToArray();

                        if (baseMethods.Length == 0)
                        {
                            if (overrideNode != null)
                            {
                                Scope.Log.LogError(new LogEntry(
                                    "no base method",
                                    NodeHelpers.HighlightEven(
                                        "method '", methodDef.Name.ToString(), "' is marked '", "override", 
                                        "', but base type '", Scope.TypeNamer.Convert(parentTy), 
                                        "' does not define any (visible) methods that match its signature."),
                                    NodeHelpers.ToSourceLocation(overrideNode.Range)));
                            }
                            else if (newNode != null
                                && EcsWarnings.RedundantNewAttributeWarning.UseWarning(Scope.Log.Options))
                            {
                                Scope.Log.LogWarning(new LogEntry(
                                    "redundant attribute",
                                    NodeHelpers.HighlightEven(
                                        "method '", methodDef.Name.ToString(), "' is marked '", "new", 
                                        "', but base type '", Scope.TypeNamer.Convert(parentTy), 
                                        "' does not define any (visible) methods that match its signature. ")
                                    .Concat(new MarkupNode[] { EcsWarnings.RedundantNewAttributeWarning.CauseNode }),
                                    NodeHelpers.ToSourceLocation(newNode.Range)));
                            }
                        }
                        else
                        {
                            if (overrideNode != null)
                            {
                                foreach (var m in baseMethods)
                                {
                                    if (!m.HasSameSignature(methodDef))
                                    {
                                        Scope.Log.LogError(new LogEntry(
                                            "signature mismatch",
                                            NodeHelpers.HighlightEven(
                                                "method '", methodDef.Name.ToString(), "' is marked '", 
                                                "override", "', but differs in return type. " +
                                                "Expected return type: ", 
                                                Scope.TypeNamer.Convert(m.ReturnType), "'."),
                                            methodDef.GetSourceLocation()));
                                    }
                                    else if (!m.GetIsVirtual())
                                    {
                                        Scope.Log.LogError(new LogEntry(
                                            "signature mismatch",
                                            NodeHelpers.HighlightEven(
                                                "method '", methodDef.Name.ToString(), "' is marked '", 
                                                "override", "', but its base method was neither '", 
                                                "abstract", "' nor '", "virtual", "'."),
                                            methodDef.GetSourceLocation()));
                                    }
                                    else
                                    {
                                        methodDef.AddBaseMethod(m.MakeGenericMethod(methodDef.GenericParameters));
                                    }
                                }
                            }
                            else if (newNode == null
                                && EcsWarnings.HiddenMemberWarning.UseWarning(Scope.Log.Options))
                            {
                                Scope.Log.LogWarning(new LogEntry(
                                    "member hiding",
                                    NodeHelpers.HighlightEven(
                                        "method '", methodDef.Name.ToString(), "' hides " +
                                        (baseMethods.Length == 1 ? "a base method" : baseMethods.Length + " base methods") +
                                        ". Consider using the '", "new", "' keyword if hiding was intentional. ")
                                    .Concat(new MarkupNode[]
                                        { 
                                            EcsWarnings.HiddenMemberWarning.CauseNode,
                                            methodDef.GetSourceLocation().CreateDiagnosticsNode()
                                        })
                                    .Concat(
                                        baseMethods
                                        .Select(m => m.GetSourceLocation())
                                        .Where(loc => loc != null)
                                        .Select(loc => loc.CreateRemarkDiagnosticsNode("hidden method: ")))));
                            }
                        }
                    }
                    else if (overrideNode != null)
                    {
                        Scope.Log.LogError(new LogEntry(
                            "syntax error",
                            NodeHelpers.HighlightEven(
                                "method '", methodDef.Name.ToString(), "' is marked '", 
                                "override", "' but its declaring type '", 
                                Scope.TypeNamer.Convert(DeclaringType), 
                                "' does not have a base type."),
                            NodeHelpers.ToSourceLocation(overrideNode.Range)));
                    }

                    // Implement interfaces.
                    // This is significantly easier than handling overrides, because
                    // no keywords are involved.
                    foreach (var inter in DeclaringType.GetInterfaces())
                    {
                        var baseMethods = funScope.GetInstanceMembers(inter, name.Item1.Name)
                            .OfType<IMethod>()
                            .Where(m => m.HasSameSignature(methodDef));

                        foreach (var m in baseMethods)
                        {
                            methodDef.AddBaseMethod(m.MakeGenericMethod(methodDef.GenericParameters));
                        }
                    }
                }
            }, methodDef =>
            {
                bool isExtern = methodDef.HasAttribute(
                    PrimitiveAttributes.Instance.ImportAttribute.AttributeType);
                bool isInterface = methodDef.DeclaringType.GetIsInterface();
                bool isAbstractOrExtern = methodDef.GetIsAbstract() || isExtern;

                if (Node.ArgCount > 3)
                {
                    if (isInterface)
                    {
                        Scope.Log.LogError(new LogEntry(
                            "syntax error",
                            NodeHelpers.HighlightEven(
                                "'", "interface", "' method '", methodDef.Name.ToString(), 
                                "' cannot have a body."),
                            methodDef.GetSourceLocation()));
                    }
                    else if (isAbstractOrExtern)
                    {
                        Scope.Log.LogError(new LogEntry(
                            "syntax error",
                            NodeHelpers.HighlightEven(
                                "method '", methodDef.Name.ToString(), "' cannot both be marked '", 
                                isExtern ? "extern" : "abstract", "' and have a body."),
                            methodDef.GetSourceLocation()));
                    }

                    // Analyze the function body.
                    var innerScope = CreateGenericScope(methodDef, Scope);
                    var funScope = CreateFunctionScope(methodDef, innerScope);
                    var localScope = new LocalScope(funScope);
                    methodDef.Body = ExpressionConverters.AutoReturn(
                        methodDef.ReturnType, Converter.ConvertExpression(Node.Args[3], localScope), 
                        NodeHelpers.ToSourceLocation(Node.Args[3].Range), innerScope);  
                }
                else
                {
                    if (!isAbstractOrExtern && !isInterface)
                    {
                        Scope.Log.LogError(new LogEntry(
                            "syntax error",
                            NodeHelpers.HighlightEven(
                                "method '", methodDef.Name.ToString(), 
                                "' must have a body because it is not marked '", 
                                "abstract", "', '", "extern", "' or '", "partial", 
                                "', and is not an '", "interface", "' member."),
                            methodDef.GetSourceLocation()));
                    }
                    methodDef.Body = EmptyStatement.Instance;
                }
            });

            // Finally, add the function to the declaring type.
            DeclaringType.AddMethod(def);

            return Scope;
        }

        /// <summary>
        /// Converts a '#cons' constructor declaration node.
        /// </summary>
        public static GlobalScope ConvertConstructor(
            LNode Node, LazyDescribedType DeclaringType, 
            GlobalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckArity(Node, 4, Scope.Log))
                return Scope;

            // Handle the constructor's name first.
            var name = NodeHelpers.ToUnqualifiedName(Node.Args[1], Scope);
            var def = new LazyDescribedMethod(name.Item1, DeclaringType, methodDef =>
            {
                methodDef.IsConstructor = true;
                methodDef.ReturnType = PrimitiveTypes.Void;

                // Take care of the generic parameters next.
                var innerScope = Scope;
                foreach (var item in name.Item2(methodDef))
                {
                    // Create generic parameters.
                    methodDef.AddGenericParameter(item);
                    innerScope = innerScope.WithBinder(innerScope.Binder.AliasType(item.Name, item));
                }

                // Attributes next.
                UpdateTypeMemberAttributes(Node.Attrs, methodDef, innerScope, Converter);
                methodDef.AddAttribute(new SourceLocationAttribute(NodeHelpers.ToSourceLocation(Node.Args[1].Range)));
                if (DeclaringType.GetIsValueType())
                {
                    // C# 'struct' constructors always perform total initialization.
                    // The optimizer can use this to improve codegen.
                    methodDef.AddAttribute(PrimitiveAttributes.Instance.TotalInitializationAttribute);
                }

                // Resolve the parameters
                AnalyzeParameters(
                       Node.Args[2].Args, methodDef, innerScope, Converter);
            }, _ => { }, methodDef =>
            {
                var innerScope = CreateGenericScope(methodDef, Scope);
                var funScope = CreateFunctionScope(methodDef, Scope);
                // Analyze the function body.
                var localScope = new LocalScope(funScope);
                methodDef.Body = ExpressionConverters.AutoReturn(
                    methodDef.ReturnType, Converter.ConvertExpression(Node.Args[3], localScope), 
                    NodeHelpers.ToSourceLocation(Node.Args[3].Range), innerScope);  
            });

            // Finally, add the function to the declaring type.
            DeclaringType.AddMethod(def);

            return Scope;
        }

        /// <summary>
        /// Converts a '#var' field declaration node.
        /// </summary>
        public static GlobalScope ConvertField(
            LNode Node, LazyDescribedType DeclaringType,
            GlobalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckMinArity(Node, 2, Scope.Log))
                return Scope;

            var attrNodes = Node.Attrs;
            var typeNode = Node.Args[0];

            if (DeclaringType.GetIsInterface())
            {
                Scope.Log.LogError(new LogEntry(
                    "syntax error",
                    NodeHelpers.HighlightEven(
                        "an '", "interface", 
                        "' cannot contain fields or constants."),
                    NodeHelpers.ToSourceLocation(Node.Range)));
            }

            // Analyze attributes lazily, but only analyze them _once_.
            // A shared lazy object does just that.
            LNode constNode = null;
            var lazyAttrPair = new Lazy<Tuple<IEnumerable<IAttribute>, bool>>(() => 
                AnalyzeTypeMemberAttributes(attrNodes, DeclaringType, Scope, Converter, node =>
            {
                if (node.IsIdNamed(CodeSymbols.Const))
                {
                    constNode = node;
                    return true;
                }
                else
                {
                    return false;
                }
            }));

            // Analyze the field type lazily, as well.
            var lazyFieldType = new Lazy<IType>(() => 
                Converter.ConvertType(typeNode, Scope));

            // Iterate over each field definition, analyze them 
            // individually.
            foreach (var item in Node.Args.Slice(1))
            {
                var decomp = NodeHelpers.DecomposeAssignOrId(item, Scope.Log);
                if (decomp == null)
                    continue;

                var valNode = decomp.Item2;
                var field = new LazyDescribedField(
                                new SimpleName(decomp.Item1.Name.Name), DeclaringType, 
                                fieldDef =>
                    {
                        // Set the field's type.
                        fieldDef.FieldType = lazyFieldType.Value;

                        // Update the attribute list.
                        UpdateTypeMemberAttributes(lazyAttrPair.Value, fieldDef);
                        fieldDef.AddAttribute(new SourceLocationAttribute(NodeHelpers.ToSourceLocation(decomp.Item1.Range)));
                        if (constNode != null)
                        {
                            fieldDef.AddAttribute(PrimitiveAttributes.Instance.ConstantAttribute);
                            if (fieldDef.IsStatic
                            && EcsWarnings.RedundantStaticAttributeWarning.UseWarning(Scope.Log.Options))
                            {
                                // Warn if a field is marked both 'const' and 'static'.
                                var staticNode = attrNodes.FirstOrDefault(x => x.IsIdNamed(CodeSymbols.Static));
                                if (staticNode != null)
                                {
                                    Scope.Log.LogWarning(new LogEntry(
                                        "redundant attribute",
                                        NodeHelpers.HighlightEven(
                                            "this '", "static", "' attribute is redundant, because '",
                                            "const", "' implies '", "static", "'. ")
                                    .Concat(new MarkupNode[]
                                        { 
                                            EcsWarnings.RedundantStaticAttributeWarning.CauseNode,
                                            NodeHelpers.ToSourceLocation(staticNode.Range).CreateDiagnosticsNode(),
                                            NodeHelpers.ToSourceLocation(constNode.Range).CreateRemarkDiagnosticsNode("'const' attribute: ")
                                        })));
                                }
                            }
                            fieldDef.IsStatic = true;
                        }

                        if (decomp.Item2 != null)
                        {
                            fieldDef.Value = Converter.ConvertExpression(
                                valNode, new LocalScope(
                                CreateTypeMemberScope(fieldDef, fieldDef.FieldType, Scope)), 
                                fieldDef.FieldType);
                        }
                    });

                // Add the field to the declaring type.
                DeclaringType.AddField(field);
            }

            return Scope;
        }

        /// <summary>
        /// Converts a '#property' property declaration node.
        /// </summary>
        public static GlobalScope ConvertProperty(
            LNode Node, LazyDescribedType DeclaringType,
            GlobalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckMinArity(Node, 4, Scope.Log)
                || !NodeHelpers.CheckMaxArity(Node, 5, Scope.Log))
                return Scope;

            bool isIndexer = Node.Args[2].ArgCount > 0;
            // Handle the parameter's name first.
            var name = NodeHelpers.ToSimpleName(Node.Args[1], Scope);

            // Analyze the propert's type here, because we may want
            // to share this with an optional backing field.
            var lazyRetType = new Lazy<IType>(() => 
                Converter.ConvertType(Node.Args[0], Scope));

            // We'll also share the location attribute, which need
            // not be resolved lazily.
            var locAttr = new SourceLocationAttribute(
                              NodeHelpers.ToSourceLocation(Node.Args[1].Range));

            // Detect auto-properties early. If we bump into one,
            // then we'll recognize that by creating a backing field,
            // which can be used when analyzing the property.
            IField backingField = null;
            if (Node.Args[3].Calls(CodeSymbols.Braces)
                && Node.Args[3].Args.Any(item => item.IsId)
                && !Node.Attrs.Any(item => item.IsIdNamed(CodeSymbols.Abstract))
                && !DeclaringType.GetIsInterface())
            {
                backingField = new LazyDescribedField(
                    new SimpleName(name.ToString() + "$value"), 
                    DeclaringType, fieldDef =>
                {
                    fieldDef.FieldType = lazyRetType.Value ?? PrimitiveTypes.Void;
                    fieldDef.IsStatic = NodeHelpers.ContainsStaticAttribute(Node.Attrs);
                    // Make the backing field private and hidden, 
                    // then assign it the enclosing property's 
                    // source location.
                    fieldDef.AddAttribute(new AccessAttribute(AccessModifier.Private));
                    fieldDef.AddAttribute(PrimitiveAttributes.Instance.HiddenAttribute);
                    fieldDef.AddAttribute(locAttr);

                    if (Node.ArgCount > 4)
                    {
                        // Optionally initialize the field
                        fieldDef.Value = Converter.ConvertExpression(
                            Node.Args[4], new LocalScope(
                            CreateTypeMemberScope(fieldDef, fieldDef.FieldType, Scope)), 
                            fieldDef.FieldType);
                    }
                });
                // Add the backing field to the declaring type.
                DeclaringType.AddField(backingField);
            }

            // Create the property/indexer.
            // Note that all indexers are actually called 'Item'.
            var def = new LazyDescribedProperty(
                          isIndexer ? new SimpleName("Item") : name, 
                          DeclaringType, propDef =>
            {
                if (lazyRetType.Value == null)
                {
                    Scope.Log.LogError(new LogEntry(
                        "type resolution",
                        NodeHelpers.HighlightEven(
                            "could not resolve property type '", 
                            Node.Args[0].ToString(), "' for property '", 
                            name.ToString(), "'."),
                        NodeHelpers.ToSourceLocation(Node.Args[0].Range)));
                }
                propDef.PropertyType = lazyRetType.Value ?? PrimitiveTypes.Void;

                // Attributes next.
                var virtAttrNodes = UpdateVirtualTypeMemberAttributes("property", Node.Attrs, propDef, Scope, Converter);
                propDef.AddAttribute(locAttr);
                if (isIndexer)
                {
                    propDef.AddAttribute(PrimitiveAttributes.Instance.IndexerAttribute);
                    if (propDef.IsStatic)
                    {
                        // Nope. Nope. Nope.
                        Scope.Log.LogError(new LogEntry(
                            "syntax error",
                            NodeHelpers.HighlightEven(
                                "indexer '", name.ToString(), 
                                "' cannot be '", "static", "'."),
                            locAttr.Location));
                        return;
                    }
                }

                // Resolve the indexer parameters.
                foreach (var item in Node.Args[2].Args)
                {
                    propDef.AddParameter(ConvertParameter(item, Scope, Converter));
                }

                // Resolve base properties.
                var basePropSpec = FindBaseProperties(
                                       propDef, name, virtAttrNodes.Item1, 
                                       virtAttrNodes.Item2, Scope);

                if (Node.Args[3].Calls(CodeSymbols.Braces))
                {
                    // Typical pre-C# 6 syntax.
                    if (Node.Args[3].ArgCount == 0)
                    {
                        // Check that we have at least one accessor.
                        Scope.Log.LogError(new LogEntry(
                            "syntax error",
                            NodeHelpers.HighlightEven(
                                "property or indexer '", name.ToString(), 
                                "' must have at least one accessor."),
                            locAttr.Location));
                        return;
                    }

                    // We may be dealing with an auto-property here.
                    // Better check for that by looking at the backing 
                    // field.
                    if (backingField != null)
                    {
                        // This definitely is an auto-property, all right.
                        if (isIndexer)
                        {
                            // Indexers cannot also be auto-properties.
                            // Let's make that abundantly clear to the
                            // user.
                            Scope.Log.LogError(new LogEntry(
                                "syntax error",
                                "an indexer cannot also be an auto-property.",
                                locAttr.Location));
                            return;
                        }

                        // Create a backing field variable now, because we'll be
                        // needing it later.
                        var backingFieldVar = GetThisOrStaticFieldVariable(backingField);

                        // Analyze the auto-property's accessors.
                        foreach (var accNode in Node.Args[3].Args)
                        {
                            // Handle attributes first, because they are specified
                            // first, and because 'get' and 'set' accessors have
                            // these in common.
                            var accAttrs = ConvertAccessorAttributes(
                                               accNode.Attrs, propDef, Scope, Converter);

                            // Now try to find out what kind of attribute we're 
                            // dealing with.
                            if (accNode.IsIdNamed(CodeSymbols.get))
                            {
                                // Synthesize a 'get' accessor.
                                var getAcc = SynthesizeAccessor(
                                                 AccessorType.GetAccessor, propDef, 
                                                 propDef.PropertyType, basePropSpec,
                                                 Scope, accAttrs);

                                getAcc.Body = new ReturnStatement(
                                    backingFieldVar.CreateGetExpression());

                                propDef.AddAccessor(getAcc);
                            }
                            else if (accNode.IsIdNamed(CodeSymbols.set))
                            {
                                // Synthesize a 'set' accessor.
                                var setAcc = SynthesizeAccessor(
                                                 AccessorType.SetAccessor, propDef, 
                                                 PrimitiveTypes.Void, basePropSpec,
                                                 Scope, accAttrs);

                                var valParam = new DescribedParameter("value", propDef.PropertyType);
                                setAcc.AddParameter(valParam);

                                setAcc.Body = new BlockStatement(new IStatement[]
                                {
                                    backingFieldVar.CreateSetStatement(
                                        CreateArgumentVariable(valParam, 0)
                                            .CreateGetExpression()),
                                    new ReturnStatement()
                                });

                                propDef.AddAccessor(setAcc);
                            }
                            else if (accNode.IsId)
                            {
                                // Explain that this identifier is not a
                                // valid accessor name.
                                LogInvalidAccessorName(Scope.Log, locAttr.Location);
                            }
                            else if (accNode.IsCall)
                            {
                                // Looks like we've encountered some kind of weird
                                // manual property/auto-property contraption.
                                // Better log an error here.
                                Scope.Log.LogError(new LogEntry(
                                    "syntax error",
                                    NodeHelpers.HighlightEven(
                                        "accessor '", accNode.Name.Name, 
                                        "' cannot have a body, because its enclosing property '", 
                                        name.Name, "' is an auto-property."),
                                    NodeHelpers.ToSourceLocation(accNode.Range)));
                            }
                            else
                            {
                                // I don't even know what to expect here.
                                Scope.Log.LogError(new LogEntry(
                                    "syntax error",
                                    NodeHelpers.HighlightEven(
                                        "unexpected syntax node '", accNode.ToString(), "'."),
                                    NodeHelpers.ToSourceLocation(accNode.Range)));
                            }
                        }
                    }
                    else
                    {
                        // We have encountered a perfectly normal
                        // property that is implemented manually.
                        foreach (var accNode in Node.Args[3].Args)
                        {
                            ConvertAccessor(accNode, propDef, basePropSpec, Scope, Converter);
                        }
                    }
                }
                else
                {
                    // C# 6 expression-bodies property syntax.
                    // We will synthesize a 'get' accessor,
                    // and set its body to the expression body.
                    var getAcc = SynthesizeAccessor(
                                     AccessorType.GetAccessor, propDef,
                                     propDef.PropertyType, basePropSpec,
                                     Scope);

                    propDef.AddAccessor(getAcc);

                    var localScope = new LocalScope(CreateFunctionScope(getAcc, Scope));
                    getAcc.Body = ExpressionConverters.AutoReturn(
                        getAcc.ReturnType, 
                        Converter.ConvertExpression(Node.Args[3], localScope),
                        locAttr.Location, Scope);
                }
            });

            // Finally, add the property to the declaring type.
            DeclaringType.AddProperty(def);

            return Scope;
        }

        private static void LogInvalidAccessorName(
            ICompilerLog Log, SourceLocation Location)
        {
            Log.LogError(new LogEntry(
                "syntax error",
                NodeHelpers.HighlightEven(
                    "expected a '", "get", "' or '", 
                    "set", "' accessor."),
                Location));
        }

        /// <summary>
        /// Creates a variable that accesses the given field. 
        /// If this field is an instance field, then the 
        /// 'this' variable is used to access the field.
        /// If the field's declaring type is generic, 
        /// then a generic instance field is accessed, for 
        /// a self-instantiated version of the declaring type.  
        /// </summary>
        private static IVariable GetThisOrStaticFieldVariable(
            IField Field)
        {
            var thisVar = new ThisVariable(Field.DeclaringType);
            var thisTy = thisVar.Type;
            if (thisTy.GetIsPointer())
                thisTy = thisTy.AsPointerType().ElementType;
            
            var genericThisTy = thisTy as GenericTypeBase;
            var genField = genericThisTy != null
                ? new GenericInstanceField(
                               Field, genericThisTy.Resolver, 
                               genericThisTy)
                : Field;

            return new FieldVariable(
                genField, 
                Field.IsStatic ? null : thisVar.CreateGetExpression());
        }

        /// <summary>
        /// Synthesizes an accessor of the given accessor 
        /// kind and return type, for the given property.
        /// </summary>
        private static DescribedBodyAccessor SynthesizeAccessor(
            AccessorType Kind, IProperty DeclaringProperty, 
            IType ReturnType, BasePropertySpec BaseProperties,
            GlobalScope Scope)
        {
            return SynthesizeAccessor(
                Kind, DeclaringProperty, ReturnType, 
                BaseProperties, Scope,
                Enumerable.Empty<IAttribute>());
        }

        /// <summary>
        /// Synthesizes an accessor of the given accessor 
        /// kind and return type, for the given property.
        /// </summary>
        private static DescribedBodyAccessor SynthesizeAccessor(
            AccessorType Kind, IProperty DeclaringProperty, 
            IType ReturnType, BasePropertySpec BaseProperties,
            GlobalScope Scope, IEnumerable<IAttribute> Attributes)
        {
            var getAcc = new DescribedBodyAccessor(
                             Kind, DeclaringProperty, ReturnType);

            getAcc.IsConstructor = false;
            getAcc.IsStatic = DeclaringProperty.IsStatic;
            foreach (var param in DeclaringProperty.IndexerParameters)
                getAcc.AddParameter(param);

            // Add the explicitly specified attributes.
            foreach (var attr in Attributes)
                getAcc.AddAttribute(attr);

            // Inherit attributes from the declaring property.
            foreach (var attr in InheritAccessorAttributes(getAcc, DeclaringProperty))
                getAcc.AddAttribute(attr);

            foreach (var m in FindBaseAccessors(getAcc, BaseProperties, Scope))
                getAcc.AddBaseMethod(m);

            return getAcc;
        }

        private static IEnumerable<IAttribute> ConvertAccessorAttributes(
            IEnumerable<LNode> Attributes, IProperty DeclaringProperty,
            GlobalScope Scope, NodeConverter Converter)
        {
            return Converter.ConvertAttributeListWithAccess(
                Attributes, DeclaringProperty.GetAccess(), 
                DeclaringProperty.DeclaringType.GetIsInterface(),
                _ => false, Scope);
        }

        private static AttributeMapBuilder InheritAccessorAttributes(
            IAccessor Accessor, IProperty DeclaringProperty)
        {
            // Inherit access and source location attributes,
            // if they haven't been specified already.
            var results = new AttributeMapBuilder();
            if (!Accessor.HasAttribute(AccessAttribute.AccessAttributeType))
            {
                results.Add(DeclaringProperty.GetAccessAttribute());
            }
            if (!Accessor.HasAttribute(SourceLocationAttribute.AccessAttributeType))
            {
                var locAttr = DeclaringProperty.GetAttribute(
                                  SourceLocationAttribute.AccessAttributeType);
                if (locAttr != null)
                    results.Add(locAttr);
            }
            if (!Accessor.GetIsVirtual() && DeclaringProperty.GetIsVirtual())
            {
                results.Add(PrimitiveAttributes.Instance.VirtualAttribute);
            }
            if (!Accessor.GetIsAbstract() && DeclaringProperty.GetIsAbstract())
            {
                results.Add(PrimitiveAttributes.Instance.AbstractAttribute);
            }
            return results;
        }

        /// <summary>
        /// Converts the given accessor declaration node.
        /// </summary>
        private static void ConvertAccessor(
            LNode Node, LazyDescribedProperty DeclaringProperty,
            BasePropertySpec BaseProperties,
            GlobalScope Scope, NodeConverter Converter)
        {
            bool isGetter = Node.Calls(CodeSymbols.get) || Node.IsIdNamed(CodeSymbols.get);
            if (!isGetter && !Node.Calls(CodeSymbols.set) && !Node.IsIdNamed(CodeSymbols.set))
            {
                // The given node is neither a 'get' nor a 'set'
                // call.
                LogInvalidAccessorName(
                    Scope.Log, 
                    NodeHelpers.ToSourceLocation(Node.Range));
                return;
            }
            
            if (!NodeHelpers.CheckMaxArity(Node, 1, Scope.Log))
                return;

            var accKind = isGetter 
                ? AccessorType.GetAccessor 
                : AccessorType.SetAccessor;
            var def = new LazyDescribedAccessor(
                          accKind, DeclaringProperty, methodDef =>
            {
                var accDef = (LazyDescribedAccessor)methodDef;
                // Analyze the attributes first.
                accDef.AddAttributes(ConvertAccessorAttributes(
                    Node.Attrs, DeclaringProperty, Scope, Converter));

                // Inherit attributes from the parent property.
                accDef.AddAttributes(InheritAccessorAttributes(accDef, DeclaringProperty));

                // Getters return the property's type,
                // setters always return value.
                accDef.ReturnType = isGetter 
                    ? DeclaringProperty.PropertyType
                    : PrimitiveTypes.Void;

                // Inherit indexer parameters from the enclosing
                // indexer/property.
                foreach (var indParam in DeclaringProperty.IndexerParameters)
                    accDef.AddParameter(indParam);

                if (!isGetter)
                    // Setters get an extra 'value' parameter.
                    accDef.AddParameter(new DescribedParameter(
                        "value", DeclaringProperty.PropertyType));
            }, methodDef =>
            {
                var accDef = (LazyDescribedAccessor)methodDef;
                foreach (var m in FindBaseAccessors(accDef, BaseProperties, Scope))
                    accDef.AddBaseMethod(m);
            }, methodDef =>
            {
                var accDef = (LazyDescribedAccessor)methodDef;
                var localScope = new LocalScope(
                    CreateFunctionScope(accDef, Scope));

                bool isAbstract = DeclaringProperty.GetIsAbstract() 
                    || DeclaringProperty.DeclaringType.GetIsInterface();

                if (Node.ArgCount == 0)
                {
                    // Accessor doesn't have a body.
                    if (!isAbstract)
                    {
                        if (DeclaringProperty.DeclaringType.GetIsInterface())
                        {
                            Scope.Log.LogError(new LogEntry(
                                "syntax error",
                                NodeHelpers.HighlightEven(
                                    "accessor '", accDef.AccessorType.Name.ToLower(), 
                                    "' cannot have a method body, because it is " +
                                    "declared in an '", "interface", "'."),
                                NodeHelpers.ToSourceLocation(Node.Range)));
                        }
                        else
                        {
                            Scope.Log.LogError(new LogEntry(
                                "syntax error",
                                NodeHelpers.HighlightEven(
                                    "accessor '", accDef.AccessorType.Name.ToLower(), 
                                    "' cannot have a method body, because it is " +
                                    "marked '", "abstract", "'."),
                                NodeHelpers.ToSourceLocation(Node.Range)));
                        }
                    }
                    accDef.Body = EmptyStatement.Instance;
                }
                else
                {
                    if (isAbstract)
                    {
                        Scope.Log.LogError(new LogEntry(
                            "syntax error",
                            NodeHelpers.HighlightEven(
                                "accessor '", accDef.AccessorType.Name.ToLower(), 
                                "' must have a method body, because it is not '",
                                "abstract", "', and its declaring type is not an '", 
                                "interface", "'."),
                            NodeHelpers.ToSourceLocation(Node.Range)));
                    }
                    // Analyze the body.
                    accDef.Body = ExpressionConverters.AutoReturn(
                        accDef.ReturnType, Converter.ConvertExpression(Node.Args[0], localScope), 
                        NodeHelpers.ToSourceLocation(Node.Args[0].Range), Scope);  
                }
            });

            // Don't forget to add this accessor to its enclosing
            // property.
            DeclaringProperty.AddAccessor(def);
        }

        /// <summary>
        /// A simple data structure that stores a property's
        /// list of base properties and interface properties.
        /// Override and new attributes can also be accessed.
        /// </summary>
        private class BasePropertySpec
        {
            public BasePropertySpec(
                IReadOnlyList<IProperty> BaseProperties, 
                IReadOnlyList<IProperty> InterfaceProperties, 
                LNode OverrideNode, LNode NewNode)
            {
                this.BaseProperties = BaseProperties;
                this.InterfaceProperties = InterfaceProperties;
                this.OverrideNode = OverrideNode;
                this.NewNode = NewNode;
            }

            public IReadOnlyList<IProperty> BaseProperties { get; private set; }

            public IReadOnlyList<IProperty> InterfaceProperties { get; private set; }

            public LNode OverrideNode { get; private set; }

            public LNode NewNode { get; private set; }
        }

        /// <summary>
        /// Tries to find base and interface implementation properties
        /// for the given property with the given name, and attribute nodes.
        /// </summary>
        private static BasePropertySpec FindBaseProperties(
            IProperty Property, SimpleName Name, 
            LNode OverrideNode, LNode NewNode, GlobalScope Scope)
        {
            var basePropList = new List<IProperty>();
            var interfacePropList = new List<IProperty>();

            // Handle overrides
            if (!Property.IsStatic)
            {
                var declTy = Property.DeclaringType;

                var funScope = CreateTypeMemberScope(
                                   Property, Property.PropertyType, Scope);

                var parentTy = declTy.GetParent();
                if (parentTy != null)
                {
                    var baseProperties = funScope.GetInstanceMembers(parentTy, Name.Name)
                        .OfType<IProperty>()
                        .Where(m => m.HasSameCallSignature(Property))
                        .ToArray();

                    if (baseProperties.Length == 0)
                    {
                        if (OverrideNode != null)
                        {
                            Scope.Log.LogError(new LogEntry(
                                "no base property",
                                NodeHelpers.HighlightEven(
                                    "property '", Name.ToString(), "' is marked '", "override", 
                                    "', but base type '", Scope.TypeNamer.Convert(parentTy), 
                                    "' does not define any (visible) properties that match its signature."),
                                NodeHelpers.ToSourceLocation(OverrideNode.Range)));
                        }
                        else if (NewNode != null
                                 && EcsWarnings.RedundantNewAttributeWarning.UseWarning(Scope.Log.Options))
                        {
                            Scope.Log.LogWarning(new LogEntry(
                                "redundant attribute",
                                NodeHelpers.HighlightEven(
                                    "property '", Name.ToString(), "' is marked '", "new", 
                                    "', but base type '", Scope.TypeNamer.Convert(parentTy), 
                                    "' does not define any (visible) properties that match its signature. ")
                                .Concat(new MarkupNode[] { EcsWarnings.RedundantNewAttributeWarning.CauseNode }),
                                NodeHelpers.ToSourceLocation(NewNode.Range)));
                        }
                    }
                    else
                    {
                        if (OverrideNode != null)
                        {
                            foreach (var m in baseProperties)
                            {
                                if (!m.HasSameSignature(Property))
                                {
                                    Scope.Log.LogError(new LogEntry(
                                        "signature mismatch",
                                        NodeHelpers.HighlightEven(
                                            "property '", Name.ToString(), "' is marked '", 
                                            "override", "', but differs in property type. " +
                                        "Expected property type: ", 
                                            Scope.TypeNamer.Convert(m.PropertyType), "'."),
                                        Property.GetSourceLocation()));
                                }
                                else
                                {
                                    basePropList.Add(m);
                                }
                            }
                        }
                        else if (NewNode == null
                                 && EcsWarnings.HiddenMemberWarning.UseWarning(Scope.Log.Options))
                        {
                            Scope.Log.LogWarning(new LogEntry(
                                "member hiding",
                                NodeHelpers.HighlightEven(
                                    "property '", Name.ToString(), "' hides " +
                                (baseProperties.Length == 1 ? "a base property" : baseProperties.Length + " base properties") +
                                ". Consider using the '", "new", "' keyword if hiding was intentional. ")
                                .Concat(new MarkupNode[]
                                { 
                                    EcsWarnings.HiddenMemberWarning.CauseNode,
                                    Property.GetSourceLocation().CreateDiagnosticsNode()
                                })
                                .Concat(
                                    baseProperties
                                    .Select(m => m.GetSourceLocation())
                                    .Where(loc => loc != null)
                                    .Select(loc => loc.CreateRemarkDiagnosticsNode("hidden property: ")))));
                        }
                    }
                }
                else if (OverrideNode != null)
                {
                    Scope.Log.LogError(new LogEntry(
                        "syntax error",
                        NodeHelpers.HighlightEven(
                            "property '", Name.ToString(), "' is marked '", 
                            "override", "' but its declaring type '", 
                            Scope.TypeNamer.Convert(declTy), 
                            "' does not have a base type."),
                        NodeHelpers.ToSourceLocation(OverrideNode.Range)));
                }

                // Implement interfaces.
                // This is significantly easier than handling overrides, because
                // no keywords are involved.
                foreach (var inter in declTy.GetInterfaces())
                {
                    var baseMethods = funScope.GetInstanceMembers(inter, Name.Name)
                        .OfType<IProperty>()
                        .Where(m => m.HasSameSignature(Property));

                    interfacePropList.AddRange(baseMethods);
                }
            }

            return new BasePropertySpec(basePropList, interfacePropList, OverrideNode, NewNode);
        }

        /// <summary>
        /// Tries to find base accessors for the base property
        /// spec.
        /// </summary>
        private static IEnumerable<IAccessor> FindBaseAccessors(
            IAccessor Accessor, BasePropertySpec Spec, GlobalScope Scope)
        {
            return FindBaseAccessors(
                Accessor, Spec.BaseProperties, Spec.InterfaceProperties, 
                Spec.OverrideNode, Spec.NewNode, Scope);
        }

        /// <summary>
        /// Tries to find base accessors for the given
        /// accessor, base properties and interfaces properties.
        /// </summary>
        private static IEnumerable<IAccessor> FindBaseAccessors(
            IAccessor Accessor, IEnumerable<IProperty> BaseProperties,
            IEnumerable<IProperty> InterfaceProperties,
            LNode OverrideNode, LNode NewNode, GlobalScope Scope)
        {   
            var declProp = Accessor.DeclaringProperty;

            var results = new List<IAccessor>();

            // Handle overrides
            if (!declProp.IsStatic)
            {
                var declTy = declProp.DeclaringType;

                var baseMethods = BaseProperties
                    .Select(p => p.GetAccessor(Accessor.AccessorType))
                    .Where(p => p != null)
                    .ToArray();

                if (baseMethods.Length == 0)
                {
                    if (OverrideNode != null)
                    {
                        Scope.Log.LogError(new LogEntry(
                            "no base accessor",
                            NodeHelpers.HighlightEven(
                                "property '", declProp.Name.ToString(), "' is marked '", "override", 
                                "', but base type '", Scope.TypeNamer.Convert(declTy.GetParent()), 
                                "' does not define any (visible) property accessors that match the '", 
                                Accessor.AccessorType.ToString().ToLower(), "' accessor's signature."),
                            NodeHelpers.ToSourceLocation(OverrideNode.Range)));
                    }
                }
                else
                {
                    if (OverrideNode != null)
                    {
                        foreach (var m in baseMethods)
                        {
                            if (!m.GetIsVirtual())
                            {
                                Scope.Log.LogError(new LogEntry(
                                    "signature mismatch",
                                    NodeHelpers.HighlightEven(
                                        "property '", declProp.Name.ToString(), "' is marked '", 
                                        "override", "', but the '", 
                                        Accessor.AccessorType.ToString().ToLower(), 
                                        "' accessor's base method was neither '", "abstract", 
                                        "' nor '", "virtual", "'."),
                                    Accessor.GetSourceLocation()));
                            }
                            else
                            {
                                results.Add(m);
                            }
                        }
                    }
                }

                // Implement interfaces.
                // This is significantly easier than handling overrides, because
                // no keywords are involved.
                results.AddRange(InterfaceProperties
                    .Select(p => p.GetAccessor(Accessor.AccessorType))
                    .Where(m => m != null));
                
            }
        
            return results;
        }
    }
}

