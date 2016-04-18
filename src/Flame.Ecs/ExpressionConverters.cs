using System;
using System.Collections.Generic;
using System.Linq;
using Flame.Compiler;
using Flame.Compiler.Expressions;
using Flame.Compiler.Statements;
using Loyc.Syntax;
using Pixie;
using Flame.Build;

namespace Flame.Ecs
{
	public static class ExpressionConverters
	{
		public static TypeOrExpression LookupUnqualifiedName(string Name, ILocalScope Scope)
		{
			var local = Scope.GetVariable(Name);
			if (local != null)
			{
				return new TypeOrExpression(local.CreateGetExpression());
			}

			return TypeOrExpression.Empty;
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

		/// <summary>
		/// Converts the given member access node (type @.).
		/// </summary>
		public static IExpression ConvertMemberAccess(LNode Node, LocalScope Scope, NodeConverter Converter)
		{
			throw new NotImplementedException();
		}

		private static string CreateExpectedSignatureDescription(
			TypeConverterBase<string> TypeNamer, IType ReturnType, 
			IType[] ArgumentTypes)
		{
			// Create a method signature, then turn that
			// into a delegate, and finally feed that to
			// the type namer.

			var descMethod = new DescribedMethod("", null, ReturnType, true);

			for (int i = 0; i < ArgumentTypes.Length; i++)
			{
				descMethod.AddParameter(
					new DescribedParameter("param" + i, ArgumentTypes[i]));
			}

			return TypeNamer.Convert(MethodType.Create(descMethod));
		}

		private static MarkupNode CreateSignatureDiff(
			TypeConverterBase<string> TypeNamer, IType[] ArgumentTypes, 
			IMethod Target)
		{
			var methodDiffBuilder = new MethodDiffComparer(TypeNamer);
			var argDiff = methodDiffBuilder.CompareArguments(ArgumentTypes, Target);

			var nodes = new List<MarkupNode>();
			if (Target.IsStatic)
			{
				nodes.Add(new MarkupNode(NodeConstants.TextNodeType, "static "));
			}
			if (Target.IsConstructor)
			{
				nodes.Add(new MarkupNode(NodeConstants.TextNodeType, "new " + TypeNamer.Convert(Target.DeclaringType)));
			}
			else
			{
				nodes.Add(new MarkupNode(NodeConstants.TextNodeType, TypeNamer.Convert(Target.ReturnType) + " " + Target.FullName));
			}

			nodes.Add(argDiff);
			return new MarkupNode("#group", nodes);
		}

		/// <summary>
		/// Converts a call-expression.
		/// </summary>
		public static IExpression ConvertCall(LNode Node, LocalScope Scope, NodeConverter Converter)
		{
			var target = Converter.ConvertExpression(Node.Target, Scope).GetEssentialExpression();
			var delegates = IntersectionExpression.GetIntersectedExpressions(target);
			var args = Node.Args.Select(item => Converter.ConvertExpression(item, Scope)).ToArray();
			var argTypes = args.GetTypes();

			// TODO: implement and use C#-specific overload resolution
			// here. (i.e. read and implement the language spec)
			var bestDelegate = delegates.GetBestDelegate(argTypes);

			if (bestDelegate == null)
			{
				var matches = target.GetMethodGroup();
				var namer = Scope.Function.Global.TypeNamer;

				var retType = matches.Any() ? matches.First().ReturnType : PrimitiveTypes.Void;
				var expectedSig = CreateExpectedSignatureDescription(namer, retType, argTypes);

				// Create an inner expression that consists of the invocation's target and arguments,
				// whose values are calculated and then popped. Said expression will return an 
				// unknown value of the return type.
				var innerStmts = new List<IStatement>();
				innerStmts.Add(new ExpressionStatement(target));
				innerStmts.AddRange(args.Select(arg => new ExpressionStatement(arg)));
				var innerExpr = new InitializedExpression(
					new BlockStatement(innerStmts), new UnknownExpression(retType));

				var log = Scope.Function.Global.Log;
				if (matches.Any())
				{
					var failedMatchesList = matches.Select(
						m => CreateSignatureDiff(namer, argTypes, m));

					var explanationNodes = NodeHelpers.HighlightEven(
                		"method call could not be resolved. " +
						"Expected signature compatible with '", expectedSig,
                		"'. Incompatible or ambiguous matches:");

					var failedMatchesNode = ListExtensions.Instance.CreateList(failedMatchesList);
					log.LogError(new LogEntry(
						"method resolution error",
						explanationNodes.Concat(new MarkupNode[] { failedMatchesNode }),
						NodeHelpers.ToSourceLocation(Node.Range)));
				}
				else
				{
					log.LogError(new LogEntry(
						"method resolution error",
						NodeHelpers.HighlightEven(
							"method call could not be resolved because the invocation's target was not recognized as a function. " +
							"Expected signature compatible with '", expectedSig,
							"', got an expression of type '", namer.Convert(target.Type), "'."),
						NodeHelpers.ToSourceLocation(Node.Range)));
				}
				return innerExpr;
			}
			else
			{
				var delegateParams = bestDelegate.GetDelegateParameterTypes().ToArray();

				var delegateArgs = new IExpression[args.Length];
				for (int i = 0; i < delegateArgs.Length; i++)
				{
					delegateArgs[i] = Scope.Function.Global.ConvertImplicit(
						args[i], delegateParams[i], NodeHelpers.ToSourceLocation(Node.Args[i].Range));
				}

				return bestDelegate.CreateDelegateInvocationExpression(delegateArgs);
			}
		}
	}
}

