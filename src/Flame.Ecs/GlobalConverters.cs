using System;
using System.Linq;
using Loyc.Syntax;
using Flame.Build;
using Flame.Build.Lazy;
using Flame.Compiler;
using Flame.Compiler.Expressions;
using Flame.Compiler.Statements;
using Flame.Compiler.Variables;
using Flame.Ecs.Syntax;
using System.Collections.Generic;
using Flame.Ecs.Diagnostics;
using Pixie;

namespace Flame.Ecs
{
    public static class GlobalConverters
    {
        /// <summary>
        /// Converts an '#import' directive.
        /// </summary>
        public static GlobalScope ConvertImportDirective(
            LNode Node, IMutableNamespace Namespace, 
            GlobalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckArity(Node, 1, Scope.Log))
                return Scope;

            bool isStatic = false;
            NodeHelpers.ConvertAttributes(Node, Scope.Log, attr =>
            {
                if (attr.IsIdNamed(CodeSymbols.Static))
                {
                    isStatic = true;
                    return true;
                }
                else
                {
                    return false;
                }
            });


            if (isStatic)
            {
                var lazyTy = new Lazy<IType>(() => Converter.ConvertCheckedType(Node.Args[0], Scope, null));
                return Scope.WithBinder(Scope.Binder.UseType(lazyTy));
            }
            else
            {
                var qualName = NodeHelpers.ToQualifiedName(Node.Args[0]);
                return Scope.WithBinder(Scope.Binder.UseNamespace(qualName));
            }
        }

        /// <summary>
        /// Converts an '#alias' directive.
        /// </summary>
        public static GlobalScope ConvertAliasDirective(
            LNode Node, IMutableNamespace Namespace, 
            GlobalScope Scope, NodeConverter Converter)
        {
            // A using alias looks like this in C# and LES, respectively:
            //
            //     using Number = System.Double;
            //     @[#filePrivate] #alias(Number = System.Double, #());
            //
            // TODO: figure out what the empty tuple means.

            if (!NodeHelpers.CheckArity(Node, 2, Scope.Log)
                || !NodeHelpers.CheckArity(Node.Args[1], 0, Scope.Log))
                return Scope;

            var assignment = Node.Args[0];
            if (!NodeHelpers.CheckArity(assignment, 2, Scope.Log))
                return Scope;

            var aliasNode = assignment.Args[0];
            var aliasedNode = assignment.Args[1];

            if (!NodeHelpers.CheckId(aliasNode, Scope.Log))
                return Scope;

            var alias = new SimpleName(aliasNode.Name.Name);
            var aliasedQualName = NodeHelpers.ToQualifiedName(aliasedNode);
            var newBinder = Scope.Binder.AliasType(
                alias,
                new Lazy<IType>(() => Converter.ConvertType(aliasedNode, Scope, null)));

            if (!aliasedQualName.IsEmpty)
            {
                newBinder = newBinder.AliasName(alias, aliasedQualName);
            }

            return Scope.WithBinder(newBinder);
        }

        /// <summary>
        /// Converts a '#namespace' node.
        /// </summary>
        public static GlobalScope ConvertNamespaceDefinition(
            LNode Node, IMutableNamespace Namespace, 
            GlobalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckArity(Node, 3, Scope.Log))
                return Scope;

            NodeHelpers.CheckEmptyAttributes(Node, Scope.Log);

            var qualName = NodeHelpers.ToQualifiedName(Node.Args[0]);

            var ns = Namespace;
            var nsScope = Scope;
            foreach (var name in qualName.Path)
            {
                ns = ns.DefineNamespace(name.ToString());
                nsScope = nsScope.WithBinder(nsScope.Binder.UseNamespace(ns.FullName));
            }

            foreach (var elem in Node.Args[2].Args)
            {
                nsScope = Converter.ConvertGlobal(elem, ns, nsScope);
            }

            return Scope;
        }

        public static GlobalScope ConvertGenericParameterDefs(
            IReadOnlyList<GenericParameterDef> Defs, 
            IGenericMember DeclaringMember,
            GlobalScope Scope, NodeConverter Converter,
            Func<SimpleName, IGenericParameter> AddGenericParameter,
            Action<IGenericParameter, IGenericConstraint> AddConstraint)
        {
            if (Defs.Count == 0)
                // Early-out here in the common case.
                return Scope;

            // First, retrieve all existing generic parameters.
            var genParameters = DeclaringMember.GenericParameters.ToList();

            // If we have more generic parameter definitions than
            // actual generic parameters, then we should convert 
            // those additional generic definitions into parameters. 
            for (int i = genParameters.Count; i < Defs.Count; i++)
            {
                genParameters.Add(AddGenericParameter(Defs[i].Name));
            }

            // Now, build a new global scope.
            var innerScope = Scope;
            for (int i = 0; i < Defs.Count; i++)
            {
                innerScope = innerScope.WithBinder(innerScope.Binder.AliasType(
                    Defs[i].Name, genParameters[i]));
            }

            IType declType = null;
            if (DeclaringMember is IType)
            {
                declType = ((IType)DeclaringMember).DeclaringNamespace as IType;
            }
            else if (DeclaringMember is ITypeMember)
            {
                declType = ((ITypeMember)DeclaringMember).DeclaringType;
            }
            var localScope = Scope.CreateLocalScope(declType);

            // And use that to convert generic parameters.
            for (int i = 0; i < Defs.Count; i++)
            {
                foreach (var constraintNode in Defs[i].Constraints)
                {
                    var constraint = constraintNode.Analyze(localScope, Converter);
                    AddConstraint(genParameters[i], constraint);
                }
            }

            // Return the global scope.
            return innerScope;
        }

        /// <summary>
        /// Converts a type.
        /// </summary>
        public static GlobalScope ConvertTypeDefinition(
            IAttribute TypeKind, LNode Node, IMutableNamespace Namespace, 
            GlobalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckArity(Node, 3, Scope.Log))
                return Scope;

            // Convert the type's name.
            var name = NameNodeHelpers.ToGenericMemberName(Node.Args[0], Scope);
            Namespace.DefineType(name.Name, (descTy, isRedefinition) =>
            {
                var innerScope = ConvertGenericParameterDefs(
                    name.GenericParameters, descTy, Scope, Converter, 
                    genParamName => 
                    {
                        var genParam = new DescribedGenericParameter(genParamName, descTy);
                        descTy.AddGenericParameter(genParam);
                        return genParam;
                    },
                    (genParam, constraint) => 
                    {
                        ((DescribedGenericParameter)genParam).AddConstraint(constraint);
                    });

                if (isRedefinition && !descTy.HasAttribute(TypeKind.AttributeType))
                {
                    // Partial type definitions must consistently be labeled
                    // 'class', 'struct' or 'interface'.
                    Scope.Log.LogError(new LogEntry(
                        "syntax error",
                        NodeHelpers.CreateRedefinitionMessage(
                            NodeHelpers.HighlightEven(
                                "'", "partial", "' type '", descTy.Name.ToString(), 
                                "' is not consistently a '", "class", "', '", "struct", 
                                "', or '", "interface", "'."),
                            NodeHelpers.ToSourceLocation(Node.Args[0].Range),
                            descTy.GetSourceLocation())));
                }

                var declTy = descTy.DeclaringNamespace as IType;

                // Analyze the attribute list.
                bool isClass = TypeKind.AttributeType.Equals(
                                   PrimitiveAttributes.Instance.ReferenceTypeAttribute.AttributeType);
                bool isVirtual = isClass;
                bool isPartial = false;
                var convAttrs = Converter.ConvertAttributeListWithAccess(
                    Node.Attrs, AccessModifier.Assembly, node =>
                {
                    if (node.IsIdNamed(CodeSymbols.Static))
                    {
                        if (!descTy.HasAttribute(PrimitiveAttributes.Instance.StaticTypeAttribute.AttributeType))
                            descTy.AddAttribute(PrimitiveAttributes.Instance.StaticTypeAttribute);
                        
                        isVirtual = false;
                        return true;
                    }
                    else if (isClass && node.IsIdNamed(CodeSymbols.Sealed))
                    {
                        isVirtual = false;
                        return true;
                    }
                    else if (node.IsIdNamed(CodeSymbols.Partial))
                    {
                        isPartial = true;
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }, Scope.CreateLocalScope(declTy));

                if (!isRedefinition)
                {
                    descTy.AddAttribute(TypeKind);
                }
                foreach (var item in convAttrs)
                {
                    descTy.AddAttribute(item);
                }
                if (isRedefinition && !isPartial)
                {
                    Scope.Log.LogError(new LogEntry(
                        "type redefinition",
                        NodeHelpers.CreateRedefinitionMessage(
                            NodeHelpers.HighlightEven(
                                "type '", descTy.Name.ToString(), 
                                "' is defined more than once, and is not '", 
                                "partial", "'."),
                            NodeHelpers.ToSourceLocation(Node.Args[0].Range),
                            descTy.GetSourceLocation())));
                }
                descTy.AddAttribute(new SourceLocationAttribute(NodeHelpers.ToSourceLocation(Node.Args[0].Range)));

                descTy.RemoveAttributes(PrimitiveAttributes.Instance.VirtualAttribute.AttributeType);
                if (isVirtual)
                {
                    descTy.AddAttribute(PrimitiveAttributes.Instance.VirtualAttribute);
                }

                // Check if the type defines any extension methods 
                // before proceeding.
                if (descTy.GetIsStaticType()
                    && Node.Args[2].Args.Any(NodeHelpers.IsExtensionMethod)
                    && !descTy.HasAttribute(
                        PrimitiveAttributes.Instance.ExtensionAttribute.AttributeType))
                {
                    descTy.AddAttribute(PrimitiveAttributes.Instance.ExtensionAttribute);
                }

                // Remember the base class.
                IType baseClass = descTy.GetParent();
                foreach (var item in Node.Args[1].Args)
                {
                    // Convert the base types.
                    var innerTy = Converter.ConvertType(item, innerScope, declTy);
                    if (innerTy == null)
                    {
                        Scope.Log.LogError(new LogEntry(
                            "type resolution",
                            NodeHelpers.HighlightEven(
                                "cannot resolve base type '", 
                                item.ToString(), "' for '", 
                                name.Name.ToString(), "'."),
                            NodeHelpers.ToSourceLocation(item.Range)));
                    }
                    else
                    {
                        if (!innerTy.GetIsInterface())
                        {
                            if (innerTy.GetIsValueType())
                            {
                                var renderer = Scope.CreateAbbreviatingRenderer(descTy, innerTy);
                                Scope.Log.LogError(new LogEntry(
                                    "invalid base type",
                                    NodeHelpers.HighlightEven(
                                        "'", renderer.Name(descTy),
                                        "' cannot inherit from '", "struct",
                                        "' type '",
                                        renderer.Name(innerTy), "'."),
                                    NodeHelpers.ToSourceLocation(item.Range)));
                            }
                            else if (innerTy.GetIsGenericParameter())
                            {
                                var renderer = Scope.CreateAbbreviatingRenderer(descTy, innerTy);
                                Scope.Log.LogError(new LogEntry(
                                    "invalid base type",
                                    NodeHelpers.HighlightEven(
                                        "'", renderer.Name(descTy), 
                                        "' cannot inherit from generic parameter '", 
                                        renderer.Name(innerTy), "'."),
                                    NodeHelpers.ToSourceLocation(item.Range)));
                            }

                            if (baseClass == null)
                            {
                                baseClass = innerTy;
                            }
                            else
                            {
                                var renderer = Scope.CreateAbbreviatingRenderer(descTy, baseClass, innerTy);
                                Scope.Log.LogError(new LogEntry(
                                    "multiple base classes",
                                    NodeHelpers.HighlightEven(
                                        "'", renderer.Name(descTy),
                                        "' cannot have multiple base classes '",
                                        renderer.Name(baseClass), "' and '",
                                        renderer.Name(innerTy), "'."),
                                    NodeHelpers.ToSourceLocation(item.Range)));
                            }
                        }
                        descTy.AddBaseType(innerTy);
                    }
                }
                return innerScope;
            }, (descTy, innerScope) =>
            {
                if (!descTy.GetIsInterface() && descTy.GetParent() == null)
                {
                    var rootType = Scope.Binder.Environment.RootType;
                    if (rootType != null)
                        descTy.AddBaseType(rootType);
                }
                return innerScope;
            }, (descTy, innerScope) =>
            {
                foreach (var item in Node.Args[2].Args)
                {
                    // Convert the type definition's members.
                    innerScope = Converter.ConvertTypeMember(item, descTy, innerScope);
                }
                return innerScope;
            }, SynthesizeParameterlessConstructor);

            return Scope;
        }

        /// <summary>
        /// Synthesizes a parameterless constructor for the given type if applicable.
        /// </summary>
        /// <param name="DeclaringType">The type to synthesize a parameterless constructor for.</param>
        /// <param name="Scope">The type's global scope.</param>
        private static void SynthesizeParameterlessConstructor(
            LazyDescribedType DeclaringType, GlobalScope Scope)
        {
            if (DeclaringType.GetIsReferenceType()
                && !DeclaringType.GetIsInterface()
                && !DeclaringType.GetIsStaticType()
                && !DeclaringType.GetConstructors().Any())
            {
                // Synthesize a parameterless constructor.
                var parameterlessCtor = new DescribedBodyMethod("this", DeclaringType, PrimitiveTypes.Void, false);
                parameterlessCtor.IsConstructor = true;

                var ctorScope = Scope.CreateFunctionScope(parameterlessCtor);

                var bodyStmts = new List<IStatement>();

                // First order of business is to find a parameterless base constructor to call.
                var parentType = DeclaringType.GetParent();

                if (parentType != null)
                {
                    var parentCtors = parentType.Methods
                        .Where(m => !m.IsStatic && m.IsConstructor)
                        .Select(m => new GetMethodExpression(m, new ThisVariable(DeclaringType).CreateGetExpression()))
                        .ToArray();

                    var invocation = OverloadResolution.CreateCheckedInvocation(
                        "parameterless base constructor",
                        parentCtors,
                        new Tuple<IExpression, SourceLocation>[] { },
                        ctorScope,
                        DeclaringType.GetSourceLocation());
                    bodyStmts.Add(new ExpressionStatement(invocation));
                }

                bodyStmts.Add(new ReturnStatement());
                parameterlessCtor.Body = new BlockStatement(bodyStmts);
                DeclaringType.AddMethod(parameterlessCtor);
            }
        }

        /// <summary>
        /// Converts a '#class' node.
        /// </summary>
        public static GlobalScope ConvertClassDefinition(
            LNode Node, IMutableNamespace Namespace, GlobalScope Scope,
            NodeConverter Converter)
        {
            return ConvertTypeDefinition(PrimitiveAttributes.Instance.ReferenceTypeAttribute, Node, Namespace, Scope, Converter);
        }

        /// <summary>
        /// Converts an '#interface' node.
        /// </summary>
        public static GlobalScope ConvertInterfaceDefinition(
            LNode Node, IMutableNamespace Namespace, GlobalScope Scope,
            NodeConverter Converter)
        {
            return ConvertTypeDefinition(PrimitiveAttributes.Instance.InterfaceAttribute, Node, Namespace, Scope, Converter);
        }

        /// <summary>
        /// Converts a '#struct' node.
        /// </summary>
        public static GlobalScope ConvertStructDefinition(
            LNode Node, IMutableNamespace Namespace, GlobalScope Scope, 
            NodeConverter Converter)
        {
            return ConvertTypeDefinition(PrimitiveAttributes.Instance.ValueTypeAttribute, Node, Namespace, Scope, Converter);
        }

        /// <summary>
        /// Converts an enum type definition, 
        /// encoded as an '#enum' node.
        /// </summary>
        public static GlobalScope ConvertEnumDefinition(
            LNode Node, IMutableNamespace Namespace, 
            GlobalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckArity(Node, 3, Scope.Log))
                return Scope;

            // Convert the type's name.
            var name = NameNodeHelpers.ToGenericMemberName(Node.Args[0], Scope);
            if (name.GenericParameters.Count > 0)
            {
                Scope.Log.LogError(new LogEntry(
                    "syntax error",
                    NodeHelpers.HighlightEven(
                        "'", "enum", "' types can't have type parameters."),
                    NodeHelpers.ToSourceLocation(Node.Args[0].Range)));
            }
            Namespace.DefineType(name.Name, (descTy, isRedefinition) =>
            {
                if (isRedefinition)
                {
                    // Don't allow 'enum' redefinitions.
                    Scope.Log.LogError(new LogEntry(
                        "type redefinition", 
                        NodeHelpers.CreateRedefinitionMessage(
                            NodeHelpers.HighlightEven(
                                "type '", "enum " + descTy.Name.ToString() + 
                                "' is defined more than once."),
                            NodeHelpers.ToSourceLocation(Node.Args[0].Range), 
                            descTy.GetSourceLocation())));
                    return;
                }

                var declTy = descTy.DeclaringNamespace as IType;
                // Analyze the attribute list.
                var convAttrs = Converter.ConvertAttributeListWithAccess(
                    Node.Attrs, AccessModifier.Assembly,
                    node => false, Scope.CreateLocalScope(declTy));
                descTy.AddAttribute(PrimitiveAttributes.Instance.EnumAttribute);
                descTy.AddAttribute(PrimitiveAttributes.Instance.ValueTypeAttribute);
                descTy.AddAttribute(new SourceLocationAttribute(NodeHelpers.ToSourceLocation(Node.Args[0].Range)));
                foreach (var item in convAttrs)
                {
                    descTy.AddAttribute(item);
                }

                // Take care of the underlying type.
                IType underlyingType = PrimitiveTypes.Int32;
                if (Node.Args[1].ArgCount > 0)
                {
                    if (Node.Args[1].ArgCount > 1)
                    {
                        Scope.Log.LogError(new LogEntry(
                            "syntax error",
                            NodeHelpers.HighlightEven(
                                "at most one underlying type may be " +
                                "specified for an '", "enum", "' type."),
                            NodeHelpers.ToSourceLocation(Node.Args[1].Args[1].Range)));
                    }

                    var item = Node.Args[1].Args[0];
                    var innerTy = Converter.ConvertType(item, Scope, declTy);
                    if (innerTy == null)
                    {
                        Scope.Log.LogError(new LogEntry(
                            "type resolution",
                            NodeHelpers.HighlightEven(
                                "cannot resolve underlying type type '", 
                                item.ToString(), "' for '", 
                                name.Name.ToString(), "'."),
                            NodeHelpers.ToSourceLocation(item.Range)));
                    }
                    else
                    {
                        if (!innerTy.GetIsInteger())
                        {
                            Scope.Log.LogError(new LogEntry(
                                "invalid underlying type",
                                NodeHelpers.HighlightEven(
                                    "the underlying type for an '", "enum", 
                                    "' must be a primitive integer type."),
                                NodeHelpers.ToSourceLocation(item.Range)));
                        }
                        underlyingType = innerTy;
                    }
                }
                descTy.AddBaseType(underlyingType);

                LazyDescribedField pred = null;
                foreach (var item in Node.Args[2].Args)
                {
                    // Convert the enum's fields.
                    var field = ConvertEnumField(
                        item, descTy, underlyingType, Scope, 
                        Converter, pred);
                    
                    if (field != null)
                    {
                        descTy.AddField(field);
                        pred = field;
                    }
                }
            });

            return Scope;
        }

        public static LazyDescribedField ConvertEnumField(
            LNode Node, IType DeclaringType, IType UnderlyingType, 
            GlobalScope Scope, NodeConverter Converter,
            LazyDescribedField Predecessor)
        {
            var decomp = NodeHelpers.DecomposeAssignOrId(Node, Scope.Log);
            if (decomp == null)
                return null;

            var valNode = decomp.Item2;
            return new LazyDescribedField(
                new SimpleName(decomp.Item1.Name.Name), DeclaringType, 
                fieldDef =>
                {
                    // Set the field's type.
                    fieldDef.FieldType = DeclaringType;
                    fieldDef.IsStatic = true;
                    fieldDef.AddAttribute(PrimitiveAttributes.Instance.ConstantAttribute);

                    // TODO: handle attributes, if any

                    IExpression valExpr = null;
                    if (decomp.Item2 != null)
                    {
                        valExpr = Converter.ConvertExpression(
                            valNode, new LocalScope(
                            TypeMemberConverters.CreateTypeMemberScope(
                                fieldDef, UnderlyingType, Scope)), 
                            UnderlyingType);

                        // Try to evaluate the expression
                        if (valExpr.Evaluate() == null)
                        {
                            // Don't use it if it's not a compile-time constant.
                            Scope.Log.LogError(new LogEntry(
                                "invalid value",
                                NodeHelpers.HighlightEven(
                                    "this '", "enum", 
                                    "' literal value was not a compile-time constant."),
                                NodeHelpers.ToSourceLocation(decomp.Item2.Range)));
                            valExpr = null;
                        }
                    }

                    if (valExpr != null)
                    {
                        fieldDef.Value = valExpr;
                    }
                    else if (Predecessor != null)
                    {
                        fieldDef.Value = new AddExpression(
                            Predecessor.Value, 
                            new StaticCastExpression(
                                new IntegerExpression((int)1), 
                                UnderlyingType)).Optimize();
                    }
                    else
                    {
                        fieldDef.Value = new DefaultValueExpression(UnderlyingType).Optimize();
                    }
                });
        }

        /// <summary>
        /// Converts an assembly attribute. These attributes are identified by their
        /// type: #assembly.
        /// </summary>
        /// <param name="Node">The assembly attribute node.</param>
        /// <param name="Namespace">The namespace that defines the assembly attribute.</param>
        /// <param name="Scope">The global scope.</param>
        /// <param name="Converter">The node converter.</param>
        /// <returns>The global scope.</returns>
        public static GlobalScope ConvertAssemblyAttribute(
            LNode Node, IMutableNamespace Namespace, 
            GlobalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckArity(Node, 1, Scope.Log))
            {
                return Scope;
            }

            var dummyType = new DescribedType(
                new SimpleName("assembly"),
                null);

            var asmAttribute = new Lazy<IEnumerable<IAttribute>>(() =>
                Converter.ConvertAttribute(
                    Node.Args[0],
                    Scope.CreateLocalScope(dummyType)));

            var shouldLogWarning = EcsWarnings.CustomAttributeIgnoredWarning
                .UseWarning(Scope.Log.Options);

            if (!Namespace.AddAssemblyAttributes(asmAttribute) && shouldLogWarning)
            {
                Scope.Log.LogWarning(
                    new LogEntry(
                        "attribute ignored",
                        EcsWarnings.CustomAttributeIgnoredWarning.CreateMessage(
                            new MarkupNode(
                                "warning_message",
                                NodeHelpers.HighlightEven(
                                    "this '", "assembly", "' attribute is not meaningful here and will be ignored. " +
                                    "Try moving it to the top-level namespace. "))),
                        NodeHelpers.ToSourceLocation(Node.Target.Range)));
            }

            return Scope;
        }
    }
}

