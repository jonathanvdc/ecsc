using System;
using Flame.Compiler;
using Flame.Compiler.Expressions;
using Flame.Compiler.Statements;
using Loyc.Syntax;
using System.Collections.Generic;

namespace Flame.Ecs
{
	public static class ExpressionConverters
	{
		public static IExpression LookupUnqualifiedName(string Name, ILocalScope Scope)
		{
			var local = Scope.GetVariable(Name);
			if (local != null)
			{
				return local.CreateGetExpression();
			}

			return null;
		}

		public static IStatement ToStatement(IExpression Expression)
		{
			return new ExpressionStatement(Expression);
		}

		public static IExpression ToExpression(IStatement Statement)
		{
			return new InitializedExpression(Statement, VoidExpression.Instance);
		}

		/// <summary>
		/// Appends a `return(void);` statement to the given function 
		/// body expression, provided the return type is either `null` 
		/// or `void`. Otherwise, the body expression's value is returned,
		/// provided that its return value is not 'void'. 
		/// </summary>
		public static IStatement AutoReturn(IType ReturnType, IExpression Body, SourceLocation Location, GlobalScope Scope)
		{
			if (ReturnType == null || ReturnType.Equals(PrimitiveTypes.Void))
				return new BlockStatement(new[] { ToStatement(Body), new ReturnStatement() });
			else if (!Body.Type.Equals(PrimitiveTypes.Void))
				return new ReturnStatement(Scope.ConvertImplicit(Body, ReturnType, Location));
			else
				return ToStatement(Body);
		}

		/// <summary>
		/// Converts a block-expression (type @`{}`).
		/// </summary>
		public static IExpression ConvertBlock(LNode Node, LocalScope Scope, NodeConverter Converter)
		{
			var innerScope = new LocalScope(Scope);

			var preStmts = new List<IStatement>();
			IExpression retExpr = null;
			var postStmts = new List<IStatement>();
			int i = 0;
			while (i < Node.ArgCount)
			{
				var item = Node.Args[i];
				i++;
				if (item.Calls(CodeSymbols.Result))
				{
					NodeHelpers.CheckArity(item, 1, Scope.Function.Global.Log);

					retExpr = Converter.ConvertExpression(item.Args[0], innerScope);
					break;
				}
				else
				{
					preStmts.Add(ToStatement(Converter.ConvertExpression(item, innerScope)));
				}
			}
			while (i < Node.ArgCount)
			{
				postStmts.Add(ToStatement(Converter.ConvertExpression(Node.Args[i], innerScope)));
				i++;
			}
			postStmts.Add(innerScope.Release());
			if (retExpr == null)
			{
				preStmts.AddRange(postStmts);
				return new InitializedExpression(
					new BlockStatement(preStmts).Simplify(), 
					VoidExpression.Instance);
			}
			else
			{
				return new InitializedExpression(
					new BlockStatement(preStmts).Simplify(), retExpr, 
					new BlockStatement(postStmts).Simplify()).Simplify();
			}
		}

		/// <summary>
		/// Converts a return-expression (type #return).
		/// </summary>
		public static IExpression ConvertReturn(LNode Node, LocalScope Scope, NodeConverter Converter)
		{
			if (Node.ArgCount == 0)
			{
				return ToExpression(new ReturnStatement());
			}
			else
			{
				NodeHelpers.CheckArity(Node, 1, Scope.Function.Global.Log);

				return ToExpression(new ReturnStatement(
					Scope.Function.Global.ConvertImplicit(
						Converter.ConvertExpression(Node.Args[0], Scope), 
						Scope.ReturnType, NodeHelpers.ToSourceLocation(Node.Args[0].Range))));
			}
		}
	}
}

