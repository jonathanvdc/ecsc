using System;
using Loyc.Syntax;
using Flame.Build;
using Flame.Compiler;

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
			
			var qualName = NodeHelpers.ToQualifiedName(Node.Args[0]);
			return Scope.WithBinder(Scope.Binder.UseNamespace(qualName));
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
	}
}

