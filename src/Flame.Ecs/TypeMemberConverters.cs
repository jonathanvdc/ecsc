using System;
using System.Linq;
using Loyc.Syntax;
using Flame.Build;
using Flame.Compiler;
using Flame.Compiler.Statements;
using System.Collections.Generic;
using Flame.Compiler.Variables;
using Pixie;

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
            var attrs = Converter.ConvertAttributeListWithAccess(
                Attributes, DeclaringType.GetIsInterface() ? AccessModifier.Public : AccessModifier.Private,
                node =>
                {
                    if (node.IsIdNamed(CodeSymbols.Static))
                    {
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
		private static FunctionScope AnalyzeParameters(
			IEnumerable<LNode> Parameters, LazyDescribedMethod Target,
			GlobalScope Scope, NodeConverter Converter)
		{
			foreach (var item in Parameters)
			{
                Target.AddParameter(ConvertParameter(item, Scope, Converter));
			}
            return CreateFunctionScope(Target, Scope);
		}

        /// <summary>
        /// Creates a function scope for the given method.
        /// </summary>
        private static FunctionScope CreateFunctionScope(
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

        private static void LogStaticVirtualMethodError(
            IMethod Method, string VirtualAttribute, 
            SourceLocation Location, ICompilerLog Log)
        {
            Log.LogError(new LogEntry(
                "syntax error",
                NodeHelpers.HighlightEven(
                    "'", "static", "' method '", Method.Name.ToString(), 
                    "' cannot be marked '", VirtualAttribute, "'."),
                Location));
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
                LNode overrideNode = null;
                LNode abstractNode = null;
                LNode virtualNode = null;
                LNode sealedNode = null;
                LNode newNode = null;
                UpdateTypeMemberAttributes(Node.Attrs, methodDef, innerScope, Converter, node =>
                {
                    if (node.IsIdNamed(CodeSymbols.Override))
                    {
                        overrideNode = node;
                        return true;
                    }
                    else if (node.IsIdNamed(CodeSymbols.Abstract))
                    {
                        abstractNode = node;
                        return true;
                    }
                    else if (node.IsIdNamed(CodeSymbols.Virtual))
                    {
                        virtualNode = node;
                        return true;
                    }
                    else if (node.IsIdNamed(CodeSymbols.Sealed))
                    {
                        sealedNode = node;
                        return true;
                    }
                    else if (node.IsIdNamed(CodeSymbols.New))
                    {
                        newNode = node;
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                });
                methodDef.AddAttribute(new SourceLocationAttribute(NodeHelpers.ToSourceLocation(Node.Args[1].Range)));

                bool isVirtual = abstractNode != null || virtualNode != null 
                    || (overrideNode != null && sealedNode == null);

                // Verify that these attributes make sense.
                if ((abstractNode != null || virtualNode != null) && sealedNode != null)
                {
                    Scope.Log.LogError(new LogEntry(
                        "syntax error",
                        NodeHelpers.HighlightEven(
                            "method '", methodDef.Name.ToString(), "' cannot be marked both '", 
                            (abstractNode != null ? "abstract" : "virtual"), 
                            "' and '", "sealed", "'."),
                        NodeHelpers.ToSourceLocation(sealedNode.Range)));
                }

                if (newNode != null && overrideNode != null)
                {
                    Scope.Log.LogError(new LogEntry(
                        "syntax error",
                        NodeHelpers.HighlightEven(
                            "method '", methodDef.Name.ToString(), "' cannot be marked both '", 
                            "new", "' and '", "override", "'."),
                        NodeHelpers.ToSourceLocation(newNode.Range)));
                }

                if (overrideNode == null && sealedNode != null)
                {
                    Scope.Log.LogError(new LogEntry(
                        "syntax error",
                        NodeHelpers.HighlightEven(
                            "method '", methodDef.Name.ToString(), "' cannot be '", 
                            "sealed", "' because it is not an '", 
                            "override", "'."),
                        NodeHelpers.ToSourceLocation(overrideNode.Range)));
                }

                if (methodDef.IsStatic)
                {
                    // Static methods can't be abstract, virtual or override.
                    if (abstractNode != null)
                        LogStaticVirtualMethodError(
                            methodDef, "abstract", 
                            NodeHelpers.ToSourceLocation(abstractNode.Range), 
                            Scope.Log);
                    if (virtualNode != null)
                        LogStaticVirtualMethodError(
                            methodDef, "virtual", 
                            NodeHelpers.ToSourceLocation(virtualNode.Range), 
                            Scope.Log);
                    if (overrideNode != null)
                        LogStaticVirtualMethodError(
                            methodDef, "override", 
                            NodeHelpers.ToSourceLocation(overrideNode.Range), 
                            Scope.Log);
                    if (newNode != null)
                        LogStaticVirtualMethodError(
                            methodDef, "new", 
                            NodeHelpers.ToSourceLocation(newNode.Range), 
                            Scope.Log);
                }

                if (abstractNode != null)
                {
                    methodDef.AddAttribute(PrimitiveAttributes.Instance.AbstractAttribute);
                    if (!DeclaringType.GetIsAbstract())
                    {
                        Scope.Log.LogError(new LogEntry(
                            "syntax error",
                            NodeHelpers.HighlightEven(
                                "method '", methodDef.Name.ToString(), "' cannot be '", 
                                "abstract", "' because its declaring type is not '", 
                                "abstract", "', either."),
                            NodeHelpers.ToSourceLocation(overrideNode.Range)));
                    }
                }

                if (isVirtual)
                    methodDef.AddAttribute(PrimitiveAttributes.Instance.VirtualAttribute);

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
				var funScope = AnalyzeParameters(
					Node.Args[2].Args, methodDef, innerScope, Converter);

                // Handle overrides
                if (!methodDef.IsStatic)
                {
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
                                    NodeHelpers.ToSourceLocation(overrideNode.Range)));
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
                }
                
                // Implement interfaces
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

                bool isExtern = methodDef.HasAttribute(
                    PrimitiveAttributes.Instance.ImportAttribute.AttributeType);
                bool isAbstractOrExtern = methodDef.GetIsAbstract() || isExtern;

                if (Node.ArgCount > 3)
                {
                    if (isAbstractOrExtern)
                    {
                        Scope.Log.LogError(new LogEntry(
                            "syntax error",
                            NodeHelpers.HighlightEven(
                                "method '", methodDef.Name.ToString(), "' cannot both be marked '", 
                                isExtern ? "extern" : "abstract", "' and have a body."),
                            methodDef.GetSourceLocation()));
                    }

                    // Analyze the function body.
                    var localScope = new LocalScope(funScope);
                    methodDef.Body = ExpressionConverters.AutoReturn(
                        methodDef.ReturnType, Converter.ConvertExpression(Node.Args[3], localScope), 
                        NodeHelpers.ToSourceLocation(Node.Args[3].Range), innerScope);  
                }
                else
                {
                    if (!isAbstractOrExtern)
                    {
                        Scope.Log.LogError(new LogEntry(
                            "syntax error",
                            NodeHelpers.HighlightEven(
                                "method '", methodDef.Name.ToString(), 
                                "' must have a body because it is not marked '", 
                                "abstract", "', '", "extern", "' or '", "partial", "'."),
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
				var funScope = AnalyzeParameters(
					Node.Args[2].Args, methodDef, innerScope, Converter);

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

            // Analyze attributes lazily, but only analyze them _once_.
            // A shared lazy object does just that.
            var lazyAttrPair = new Lazy<Tuple<IEnumerable<IAttribute>, bool>>(() => 
                AnalyzeTypeMemberAttributes(attrNodes, DeclaringType, Scope, Converter));

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

                        if (decomp.Item2 != null)
                        {
                            fieldDef.Value = Converter.ConvertExpression(
                                valNode, new LocalScope(new FunctionScope(
                                    Scope, DeclaringType, null,
                                    fieldDef.FieldType, 
                                    new Dictionary<string, IVariable>())), 
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
            if (!NodeHelpers.CheckArity(Node, 4, Scope.Log))
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
                && Node.Args[3].Args.Any(item => item.IsId))
            {
                backingField = new LazyDescribedField(
                    new SimpleName(name.ToString() + "$value"), 
                    DeclaringType, fieldDef =>
                {
                    fieldDef.FieldType = lazyRetType.Value ?? PrimitiveTypes.Void;
                    // Make the backing field private and hidden, 
                    // then assign it the enclosing property's 
                    // source location.
                    fieldDef.AddAttribute(new AccessAttribute(AccessModifier.Private));
                    fieldDef.AddAttribute(PrimitiveAttributes.Instance.HiddenAttribute);
                    fieldDef.AddAttribute(locAttr);
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
                UpdateTypeMemberAttributes(Node.Attrs, propDef, Scope, Converter);
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
                                    propDef.PropertyType, accAttrs);

                                getAcc.Body = new ReturnStatement(
                                    backingFieldVar.CreateGetExpression());

                                propDef.AddAccessor(getAcc);
                            }
                            else if (accNode.IsIdNamed(CodeSymbols.set))
                            {
                                // Synthesize a 'set' accessor.
                                var setAcc = SynthesizeAccessor(
                                    AccessorType.SetAccessor, propDef, 
                                    PrimitiveTypes.Void, accAttrs);

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
                            ConvertAccessor(accNode, propDef, Scope, Converter);
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
                        propDef.PropertyType);

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
            IType ReturnType)
        {
            return SynthesizeAccessor(
                Kind, DeclaringProperty, ReturnType, 
                Enumerable.Empty<IAttribute>());
        }

        /// <summary>
        /// Synthesizes an accessor of the given accessor 
        /// kind and return type, for the given property.
        /// </summary>
        private static DescribedBodyAccessor SynthesizeAccessor(
            AccessorType Kind, IProperty DeclaringProperty, 
            IType ReturnType, IEnumerable<IAttribute> Attributes)
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

            return getAcc;
        }

        private static IEnumerable<IAttribute> ConvertAccessorAttributes(
            IEnumerable<LNode> Attributes, IProperty DeclaringProperty,
            GlobalScope Scope, NodeConverter Converter)
        {
            return Converter.ConvertAttributeListWithAccess(
                Attributes, DeclaringProperty.GetAccess(), 
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
            return results;
        }

        /// <summary>
        /// Converts the given accessor declaration node.
        /// </summary>
        private static void ConvertAccessor(
            LNode Node, LazyDescribedProperty DeclaringProperty,
            GlobalScope Scope, NodeConverter Converter)
        {
            bool isGetter = Node.Calls(CodeSymbols.get);
            if (!isGetter && !Node.Calls(CodeSymbols.set))
            {
                // The given node is neither a 'get' nor a 'set'
                // call.
                LogInvalidAccessorName(
                    Scope.Log, 
                    NodeHelpers.ToSourceLocation(Node.Range));
                return;
            }

            if (!NodeHelpers.CheckArity(Node, 1, Scope.Log))
                return;

            var accKind = isGetter 
                ? AccessorType.GetAccessor 
                : AccessorType.SetAccessor;
            var def = new LazyDescribedAccessor(
                accKind, DeclaringProperty, accDef =>
            {
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
                
                var localScope = new LocalScope(
                    CreateFunctionScope(accDef, Scope));

                // Analyze the body.
                accDef.Body = ExpressionConverters.AutoReturn(
                    accDef.ReturnType, Converter.ConvertExpression(Node.Args[0], localScope), 
                    NodeHelpers.ToSourceLocation(Node.Args[0].Range), Scope);  
            });

            // Don't forget to add this accessor to its enclosing
            // property.
            DeclaringProperty.AddAccessor(def);
        }
	}
}

