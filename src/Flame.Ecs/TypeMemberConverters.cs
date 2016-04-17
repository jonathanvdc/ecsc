using System;
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
			var paramTy = Converter.ConvertType(Node.Args[0], Scope);
			var name = NodeHelpers.ToUnqualifiedName(Node.Args[1], Scope);
			var attrs = Converter.ConvertAttributeList(Node.Attrs, node =>
			{
				if (node.IsIdNamed(CodeSymbols.Ref) || node.IsIdNamed(CodeSymbols.Out))
				{
					paramTy = paramTy.MakePointerType(PointerKind.ReferencePointer);
					return true;
				}
				else
				{
					return false;
				}
			}, Scope);
			var descParam = new DescribedParameter(name.Item1, paramTy);
			foreach (var item in attrs)
			{
				descParam.AddAttribute(item);
			}
			return descParam;
		}

		/// <summary>
		/// Converts an '#fn' function declaration node.
		/// </summary>
		public static GlobalScope ConvertFunction(
			LNode Node, DescribedType DeclaringType, 
			GlobalScope Scope, NodeConverter Converter)
		{
			if (!NodeHelpers.CheckArity(Node, 4, Scope.Log))
				return Scope;

			// Handle the function's name first.
			var name = NodeHelpers.ToUnqualifiedName(Node.Args[1], Scope);
			var methodDef = new DescribedBodyMethod(name.Item1, DeclaringType);

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
					"unresolved return type",
					NodeHelpers.HighlightEven("could not resolve return type '", Node.Args[0].ToString(), "' for method '", name.Item1, "'."),
					NodeHelpers.ToSourceLocation(Node.Args[0].Range)));
			}
			else
			{
				methodDef.ReturnType = retType;
			}

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
				paramVarDict[parameter.Name] = new ArgumentVariable(parameter, paramIndex);
				paramIndex++;
			}

			// Analyze the function body.
			var funScope = new FunctionScope(innerScope, thisTy, paramVarDict);
			methodDef.Body = ExpressionConverters.AutoReturn(
				methodDef.ReturnType, Converter.ConvertExpression(Node.Args[3], funScope), 
				NodeHelpers.ToSourceLocation(Node.Args[3].Range), innerScope);			

			// Finally, add the function to the declaring type.
			DeclaringType.AddMethod(methodDef);

			return Scope;
		}
	}
}

