using System;
using Loyc.Syntax;
using Flame.Build;
using Flame.Build.Lazy;
using Flame.Compiler;
using Flame.Compiler.Expressions;

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
			var name = NodeHelpers.ToUnqualifiedName(Node.Args[0], Scope);
			Namespace.DefineType(name.Item1, descTy =>
			{
				var innerScope = Scope;
				foreach (var item in name.Item2(descTy))
				{
					// Create generic parameters.
					descTy.AddGenericParameter(item);
					innerScope = innerScope.WithBinder(innerScope.Binder.AliasType(item.Name, item));
				}

				// Analyze the attribute list.
				bool isClass = TypeKind.AttributeType.Equals(
					PrimitiveAttributes.Instance.ReferenceTypeAttribute.AttributeType);
				bool isVirtual = isClass;
				var convAttrs = Converter.ConvertAttributeListWithAccess(
					               Node.Attrs, AccessModifier.Assembly, node =>
				{
					if (node.IsIdNamed(CodeSymbols.Static))
					{
						descTy.AddAttribute(PrimitiveAttributes.Instance.StaticTypeAttribute);
						isVirtual = false;
						return true;
					}
					else if (isClass && node.IsIdNamed(CodeSymbols.Sealed))
					{
						isVirtual = false;
						return true;
					}
					else
					{
						return false;
					}
				}, Scope);
                descTy.AddAttribute(TypeKind);
                descTy.AddAttribute(new SourceLocationAttribute(NodeHelpers.ToSourceLocation(Node.Args[0].Range)));
                foreach (var item in convAttrs)
				{
					descTy.AddAttribute(item);
				}
				if (isVirtual)
				{
					descTy.AddAttribute(PrimitiveAttributes.Instance.VirtualAttribute);
				}

                // Remember the base class.
                IType baseClass = null;
				foreach (var item in Node.Args[1].Args)
				{
					// Convert the base types.
					var innerTy = Converter.ConvertType(item, Scope);
					if (innerTy == null)
					{
						Scope.Log.LogError(new LogEntry(
							"type resolution",
							NodeHelpers.HighlightEven(
                                "could not resolve base type '", 
                                item.ToString(), "' for '", 
                                name.Item1.ToString(), "'."),
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

                if (baseClass == null && !descTy.GetIsInterface())
                {
                    var rootType = Scope.Binder.Environment.RootType;
                    if (rootType != null)
                        descTy.AddBaseType(rootType);
                }

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
            var name = NodeHelpers.ToUnqualifiedName(Node.Args[0], Scope);
            if (name.Item1.TypeParameterCount > 0)
            {
                Scope.Log.LogError(new LogEntry(
                    "syntax error",
                    NodeHelpers.HighlightEven(
                        "'", "enum", "' types can't have type parameters."),
                    NodeHelpers.ToSourceLocation(Node.Args[0].Range)));
            }
            Namespace.DefineType(name.Item1, descTy =>
            {
                // Analyze the attribute list.
                var convAttrs = Converter.ConvertAttributeListWithAccess(
                    Node.Attrs, AccessModifier.Assembly, node => false, Scope);
                descTy.AddAttribute(PrimitiveAttributes.Instance.EnumAttribute);
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
                                name.Item1.ToString(), "'."),
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
                            new Int32Expression(1), 
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

