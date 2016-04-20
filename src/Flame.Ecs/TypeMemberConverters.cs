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
					NodeHelpers.HighlightEven("could not resolve parameter type '", Node.Args[0].ToString(), "' for parameter '", name.Item1, "'."),
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
			var def = new LazyDescribedMethod(name.Item1, DeclaringType, methodDef =>
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
				var attrs = Converter.ConvertAttributeListWithAccess(
	                Node.Attrs, DeclaringType.GetIsInterface() ? AccessModifier.Public : AccessModifier.Private,
	                node =>
				{
					if (node.IsIdNamed(CodeSymbols.Static))
					{
						methodDef.IsStatic = true;
						return true;
					}
					else
					{
						return false;
					}
				}, innerScope);
				foreach (var item in attrs)
				{
					methodDef.AddAttribute(item);
				}

				// Resolve the return type.
				var retType = Converter.ConvertType(Node.Args[0], innerScope);
				if (retType == null)
				{
					Scope.Log.LogError(new LogEntry(
						"type resolution",
						NodeHelpers.HighlightEven(
							"could not resolve return type '", 
							Node.Args[0].ToString(), "' for method '", name.Item1, "'."),
						NodeHelpers.ToSourceLocation(Node.Args[0].Range)));
					retType = PrimitiveTypes.Void;
				}
				methodDef.ReturnType = retType;

				// Resolve the parameters
				var thisTy = ThisVariable.GetThisType(DeclaringType);
				var paramVarDict = new Dictionary<string, IVariable>();
				if (!methodDef.IsStatic)
				{
					paramVarDict[CodeSymbols.This.Name] = new ThisVariable(thisTy);
				}
				int paramIndex = 0;
				foreach (var item in Node.Args[2].Args)
				{
					var parameter = ConvertParameter(item, innerScope, Converter);
					methodDef.AddParameter(parameter);
					var argVar = new ArgumentVariable(parameter, paramIndex);
					var ptrVarType = parameter.ParameterType.AsPointerType();
					if (ptrVarType != null && ptrVarType.PointerKind.Equals(PointerKind.ReferencePointer))
					{
						paramVarDict[parameter.Name] = new AtAddressVariable(
							argVar.CreateGetExpression());
					}
					else
					{
						paramVarDict[parameter.Name] = argVar;
					}
					paramIndex++;
				}

				// Analyze the function body.
				var funScope = new FunctionScope(innerScope, thisTy, retType, paramVarDict);
				var localScope = new LocalScope(funScope);
				methodDef.Body = ExpressionConverters.AutoReturn(
					methodDef.ReturnType, Converter.ConvertExpression(Node.Args[3], localScope), 
					NodeHelpers.ToSourceLocation(Node.Args[3].Range), innerScope);	
			});

			// Finally, add the function to the declaring type.
			DeclaringType.AddMethod(def);

			return Scope;
		}
	}
}

