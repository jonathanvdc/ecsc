using System;
using System.Linq;
using Loyc.Syntax;
using Flame.Build;
using Flame.Build.Lazy;
using Flame.Compiler;
using Flame.Compiler.Expressions;
using Flame.Ecs.Syntax;
using System.Collections.Generic;

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
                var lazyTy = new Lazy<IType>(() => Converter.ConvertCheckedType(Node.Args[0], Scope));
                return Scope.WithBinder(Scope.Binder.UseType(lazyTy));
            }
            else
            {
                var qualName = NodeHelpers.ToQualifiedName(Node.Args[0]);
                return Scope.WithBinder(Scope.Binder.UseNamespace(qualName));
            }
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
                ns = Namespace.DefineNamespace(name.ToString());
                nsScope = Scope.WithBinder(Scope.Binder.UseNamespace(ns.FullName));
            }

            foreach (var elem in Node.Args[2].Args)
            {
                nsScope = Converter.ConvertGlobal(elem, Namespace, nsScope);
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

            // And use that to convert generic parameters.
            for (int i = 0; i < Defs.Count; i++)
            {
                foreach (var constraintNode in Defs[i].Constraints)
                {
                    var constraint = constraintNode.Analyze(innerScope, Converter);
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
                }, Scope);

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
                    var innerTy = Converter.ConvertType(item, innerScope);
                    if (innerTy == null)
                    {
                        Scope.Log.LogError(new LogEntry(
                            "type resolution",
                            NodeHelpers.HighlightEven(
                                "could not resolve base type '", 
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
                                Scope.Log.LogError(new LogEntry(
                                    "invalid base type",
                                    NodeHelpers.HighlightEven(
                                        "'", Scope.TypeNamer.Convert(descTy), 
                                        "' cannot inherit from '", "struct", 
                                        "' type '", 
                                        Scope.TypeNamer.Convert(innerTy), "'."),
                                    NodeHelpers.ToSourceLocation(item.Range)));
                            }
                            else if (innerTy.GetIsGenericParameter())
                            {
                                Scope.Log.LogError(new LogEntry(
                                    "invalid base type",
                                    NodeHelpers.HighlightEven(
                                        "'", Scope.TypeNamer.Convert(descTy), 
                                        "' cannot inherit from generic parameter '", 
                                        Scope.TypeNamer.Convert(innerTy), "'."),
                                    NodeHelpers.ToSourceLocation(item.Range)));
                            }

                            if (baseClass == null)
                            {
                                baseClass = innerTy;
                            }
                            else
                            {
                                Scope.Log.LogError(new LogEntry(
                                    "multiple base classes",
                                    NodeHelpers.HighlightEven(
                                        "'", Scope.TypeNamer.Convert(descTy), 
                                        "' cannot have multiple base classes '", 
                                        Scope.TypeNamer.Convert(baseClass), "' and '", 
                                        Scope.TypeNamer.Convert(innerTy), "'."),
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
            });

            return Scope;
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

                // Analyze the attribute list.
                var convAttrs = Converter.ConvertAttributeListWithAccess(
                                    Node.Attrs, AccessModifier.Assembly, node => false, Scope);
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
                    var innerTy = Converter.ConvertType(item, Scope);
                    if (innerTy == null)
                    {
                        Scope.Log.LogError(new LogEntry(
                            "type resolution",
                            NodeHelpers.HighlightEven(
                                "could not resolve underlying type type '", 
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
    }
}

