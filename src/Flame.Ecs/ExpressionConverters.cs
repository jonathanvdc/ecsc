using System;
using System.Collections.Generic;
using System.Linq;
using Flame.Compiler;
using Flame.Compiler.Expressions;
using Flame.Compiler.Statements;
using Loyc.Syntax;
using Pixie;
using Flame.Build;
using Flame.Compiler.Variables;

namespace Flame.Ecs
{
	public static class ExpressionConverters
	{
		private static IExpression LookupUnqualifiedNameExpression(string Name, ILocalScope Scope)
		{
			var local = Scope.GetVariable(Name);
			if (local != null)
			{
				return local.CreateGetExpression();
			}

			return null;
		}

		private static IEnumerable<IType> LookupUnqualifiedNameTypes(QualifiedName Name, ILocalScope Scope)
		{
			var ty = Scope.Function.Global.Binder.BindType(Name);
			if (ty == null)
				return Enumerable.Empty<IType>();
			else
				return new IType[] { ty };
		}

		public static TypeOrExpression LookupUnqualifiedName(string Name, ILocalScope Scope)
		{
			var qualName = new QualifiedName(Name);
			return new TypeOrExpression(
				LookupUnqualifiedNameExpression(Name, Scope),
				LookupUnqualifiedNameTypes(qualName, Scope),
				qualName);
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
		/// Returns the variable whose address is loaded by the
		/// given expression, if the expression is a get-variable
		/// expression. Otherwise, null.
		/// </summary>
		public static IVariable AsVariable(IExpression Expression)
		{
			var innerExpr = Expression.GetEssentialExpression();
			if (innerExpr is IVariableNode)
			{
				var varNode = (IVariableNode)innerExpr;
				if (varNode.Action == VariableNodeAction.Get)
				{
					return varNode.GetVariable();
				}
			}
			return null;
		}

		/// <summary>
		/// Creates an expression that represents an address to 
		/// a storage location that contains the given expression.
		/// If this expression is a variable, then an address to said
		/// variable is returned. Otherwise, a temporary is created,
		/// and said temporary's address is returned.
		/// </summary>
		public static IExpression ToValueAddress(IExpression Expression)
		{
			var variable = AsVariable(Expression);
			if (variable is IUnmanagedVariable)
			{
				return ((IUnmanagedVariable)variable).CreateAddressOfExpression();
			}
			var temp = new LocalVariable("tmp", Expression.Type);
			return new InitializedExpression(
				temp.CreateSetStatement(Expression), 
				temp.CreateAddressOfExpression());
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
		/// Creates an expression that can be used
		/// as target object for a member-access expression.
		/// </summary>
		private static IExpression AsTargetObject(IExpression Expr)
		{
			if (Expr == null)
				return null;
			else if (Expr.Type.GetIsReferenceType())
				return Expr;
			else
				return ToValueAddress(Expr);
		}

		/// <summary>
		/// Accesses the given type member on the given target
		/// expression.
		/// </summary>
		private static IExpression AccessMember(
			IExpression Target, ITypeMember Member, GlobalScope Scope)
		{
			if (Member is IField)
			{
				return new FieldVariable(
					(IField)Member, AsTargetObject(Target)).CreateGetExpression();
			}
			else if (Member is IProperty)
			{
				return new PropertyVariable(
					(IProperty)Member, AsTargetObject(Target)).CreateGetExpression();
			}
			else if (Member is IMethod)
			{
				var method = (IMethod)Member;
				if (Member.GetIsExtension() && Target != null)
				{
					return new GetExtensionMethodExpression(
						method, AsTargetObject(Target));
				}
				else
				{
					return new GetMethodExpression(
						method, AsTargetObject(Target));
				}
			}
			else
			{
				// We have no idea what to do with this.
				// Maybe pretending it doesn't exist
				// is an acceptable solution here.
				return null;
			}
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
		public static TypeOrExpression ConvertMemberAccess(
							LNode Node, LocalScope Scope, NodeConverter Converter)
		{
			if (!NodeHelpers.CheckArity(Node, 2, Scope.Function.Global.Log))
				return TypeOrExpression.Empty;

			var target = Converter.ConvertTypeOrExpression(Node.Args[0], Scope);

			if (!Node.Args[1].IsId)
			{
				Scope.Function.Global.Log.LogError(new LogEntry(
					"syntax error",
					"expected an identifier on the right-hand side of a member access expression.",
					NodeHelpers.ToSourceLocation(Node.Range)));
				return TypeOrExpression.Empty;
			}

			var ident = Node.Args[1].Name.Name;


			// First, try to resolve member-access expressions, which
			// look like this (instance member access):
			//
			//     <expr>.<identifier>
			// 
			// or like this (static member access):
			//
			//     <type>.<identifier>
			//
			var exprSet = new List<IExpression>();
			if (target.IsExpression)
			{
				var targetTy = target.Expression.Type;
				var members = Scope.Function.GetInstanceMembers(targetTy, ident);

				foreach (var item in members)
				{
					var acc = AccessMember(target.Expression, item, Scope.Function.Global);
					if (acc != null)
						exprSet.Add(acc);
				}
			}

			if (target.IsType)
			{
				foreach (var ty in target.Types)
				{
					var members = Scope.Function.GetStaticMembers(ty, ident);

					foreach (var item in members)
					{
						var acc = AccessMember(null, item, Scope.Function.Global);
						if (acc != null)
							exprSet.Add(acc);
					}
				}
			}

			IExpression expr = exprSet.Count > 0 
				? IntersectionExpression.Create(exprSet)
				: null;

			// Next, we'll handle namespaces, which are 
			// really just qualified names.
			var nsName = target.IsNamespace 
				? new QualifiedName(ident).Qualify(target.Namespace)
				: null;

			// Finally, let's do types.
			// Qualified type names can look like:
			//
			//     <type>.<nested-type>
			//
			// or, more commonly:
			//
			//     <namespace>.<type>
			//
			// We're not assuming that type names 
			// and namespace names don't overlap 
			// here.
			var typeSet = new HashSet<IType>();
			foreach (var ty in target.Types)
			{
				if (ty is INamespace)
				{
					typeSet.UnionWith(((INamespace)ty).Types.Where(item => item.Name == ident));
				}
			}
			if (target.IsNamespace)
			{
				var topLevelTy = Scope.Function.Global.Binder.BindType(nsName);
				if (topLevelTy != null)
					typeSet.Add(topLevelTy);
			}

			return new TypeOrExpression(expr, typeSet, nsName);
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
						"method resolution",
						explanationNodes.Concat(new MarkupNode[] { failedMatchesNode }),
						NodeHelpers.ToSourceLocation(Node.Range)));
				}
				else
				{
					log.LogError(new LogEntry(
						"method resolution",
						NodeHelpers.HighlightEven(
							"method call could not be resolved because the call's target was not invocable. " +
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

		/// <summary>
		/// Creates a converter that analyzes binary operator nodes.
		/// </summary>
		public static Func<LNode, LocalScope, NodeConverter, IExpression> CreateBinaryOpConverter(Operator Op)
		{
			return (node, scope, conv) =>
			{
				if (!NodeHelpers.CheckArity(node, 2, scope.Function.Global.Log))
					return VoidExpression.Instance;
					
				// var lhs = conv.ConvertExpression(node.Args[0], scope);
				// var rhs = conv.ConvertExpression(node.Args[1], scope);

				// TODO: actually implement this

				scope.Function.Global.Log.LogError(new LogEntry(
					"operators not yet implemented",
					"binary operator resolution has not been implemented yet. Sorry. :/",
					NodeHelpers.ToSourceLocation(node.Range)));

				return VoidExpression.Instance;
			};
		}
	}
}

