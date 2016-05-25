using System;
using System.Linq;
using Loyc.Syntax;
using Flame.Build;
using Flame.Compiler;
using Flame.Compiler.Statements;
using System.Collections.Generic;
using Flame.Compiler.Variables;

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
            GlobalScope Scope, NodeConverter Converter)
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
                        return false;
                    }
                }, Scope);
            return Tuple.Create<IEnumerable<IAttribute>, bool>(
                attrs.ToArray(), isStatic);
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
            foreach (var item in Attributes.Item1)
            {
                Target.AddAttribute(item);
            }
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
		/// Analyzes the given parameter list for the
		/// given described method.
		/// </summary>
		private static FunctionScope AnalyzeParameters(
			IEnumerable<LNode> Parameters, LazyDescribedMethod Target,
			GlobalScope Scope, NodeConverter Converter)
		{
			var thisTy = ThisVariable.GetThisType(Target.DeclaringType);
			var paramVarDict = new Dictionary<string, IVariable>();
			if (!Target.IsStatic)
			{
				paramVarDict[CodeSymbols.This.Name] = ThisReferenceVariable.Instance.Create(thisTy);
			}
			int paramIndex = 0;
			foreach (var item in Parameters)
			{
				var parameter = ConvertParameter(item, Scope, Converter);
				Target.AddParameter(parameter);
				var argVar = new ArgumentVariable(parameter, paramIndex);
				var ptrVarType = parameter.ParameterType.AsPointerType();
				if (ptrVarType != null && ptrVarType.PointerKind.Equals(PointerKind.ReferencePointer))
				{
                    paramVarDict[parameter.Name.ToString()] = new AtAddressVariable(
						argVar.CreateGetExpression());
				}
				else
				{
                    paramVarDict[parameter.Name.ToString()] = argVar;
				}
				paramIndex++;
			}
            return new FunctionScope(Scope, thisTy, Target, Target.ReturnType, paramVarDict);
		}

		/// <summary>
		/// Converts an '#fn' function declaration node.
		/// </summary>
		public static GlobalScope ConvertFunction(
			LNode Node, LazyDescribedType DeclaringType, 
			GlobalScope Scope, NodeConverter Converter)
		{
			if (!NodeHelpers.CheckArity(Node, 4, Scope.Log))
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

				// Attributes next.
				UpdateTypeMemberAttributes(Node.Attrs, methodDef, innerScope, Converter);
                methodDef.AddAttribute(new SourceLocationAttribute(NodeHelpers.ToSourceLocation(Node.Args[1].Range)));

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
	}
}

