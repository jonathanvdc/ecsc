﻿using System;
using System.Collections.Generic;
using System.Linq;
using Flame.Compiler;
using Flame.Compiler.Expressions;
using Flame.Compiler.Statements;
using Loyc.Syntax;
using Pixie;
using Flame.Build;
using Flame.Compiler.Variables;
using Flame.Compiler.Emit;

namespace Flame.Ecs
{
	public static class ExpressionConverters
	{
        /// <summary>
        /// Retrieves the 'this' variable from the given 
        /// local scope. 
        /// </summary>
        public static IVariable GetThisVariable(ILocalScope Scope)
        {
            return Scope.GetVariable(CodeSymbols.This.Name);
        }

		private static IExpression LookupUnqualifiedNameExpression(
            string Name, ILocalScope Scope)
		{
            // Early-out for local variables.
			var local = Scope.GetVariable(Name);
			if (local != null)
			{
				return local.CreateGetExpression();
			}

            var declType = Scope.Function.CurrentType;

            if (declType == null)
                return null;

            // Create a set of potential results.
            var exprSet = new HashSet<IExpression>();
            foreach (var item in Scope.Function.GetStaticMembers(declType, Name))
            {
                var acc = AccessMember(null, item, Scope.Function.Global);
                if (acc != null)
                    exprSet.Add(acc);
            }

            var thisVar = GetThisVariable(Scope);
            if (GetThisVariable(Scope) != null)
            {
                foreach (var item in Scope.Function.GetInstanceMembers(declType, Name))
                {
                    var acc = AccessMember(thisVar.CreateGetExpression(), item, Scope.Function.Global);
                    if (acc != null)
                        exprSet.Add(acc);
                }
            }

            return exprSet.Count > 0 
                ? IntersectionExpression.Create(exprSet)
                : null;
		}

        /// <summary>
        /// Creates a member-access expression for the given
        /// accessed member, list of type arguments, target
        /// expression, scope and location. If this member is 
        /// a method, then it is written to the MethodResult
        /// variable. If this operation succeeds, then the
        /// result is added to the given set.
        /// </summary>
        private static void CreateMemberAccess(
            ITypeMember Member, IReadOnlyList<IType> TypeArguments, 
            IExpression TargetExpression, HashSet<IExpression> Results,
            GlobalScope Scope, SourceLocation Location, ref IMethod MethodResult)
        {
            var member = Member;
            MethodResult = member as IMethod;
            if (MethodResult != null)
            {
                if (!CheckGenericConstraints(MethodResult, TypeArguments, Scope, Location))
                    // Just ignore this method for now.
                    return;

                member = MethodResult.MakeGenericMethod(TypeArguments);
            }
            else if (TypeArguments.Count > 0)
            {
                LogCannotInstantiate(Member.Name.ToString(), Scope, Location);
                return;
            }

            var acc = AccessMember(TargetExpression, member, Scope);
            if (acc != null)
                Results.Add(acc);
        }

        private static IExpression LookupUnqualifiedNameExpressionInstance(
            string Name, IReadOnlyList<IType> TypeArguments, ILocalScope Scope,
            SourceLocation Location)
        {
            var declType = Scope.Function.CurrentType;

            if (declType == null)
                return null;

            IMethod method = null;

            // Create a set of potential results.
            var exprSet = new HashSet<IExpression>();
            foreach (var item in Scope.Function.GetStaticMembers(declType, Name))
            {
                CreateMemberAccess(
                    item, TypeArguments, null, exprSet, 
                    Scope.Function.Global, Location, ref method);
            }

            var thisVar = GetThisVariable(Scope);
            if (GetThisVariable(Scope) != null)
            {
                foreach (var item in Scope.Function.GetInstanceMembers(declType, Name))
                {
                    CreateMemberAccess(
                        item, TypeArguments, thisVar.CreateGetExpression(), exprSet, 
                        Scope.Function.Global, Location, ref method);
                }
            }

            if (exprSet.Count == 0 && method != null)
            {
                // We want to provide a diagnostic here if we
                // encountered a method, but it was not given
                // the right amount of type arguments.
                LogGenericArityMismatch(method, TypeArguments.Count, Scope.Function.Global, Location);
            }

            return exprSet.Count > 0 
                ? IntersectionExpression.Create(exprSet)
                : null;
        }

		private static IEnumerable<IType> LookupUnqualifiedNameTypes(QualifiedName Name, ILocalScope Scope)
		{
			var ty = Scope.Function.Global.Binder.BindType(Name);
			if (ty == null)
				return Enumerable.Empty<IType>();
			else
				return new IType[] { ty };
		}

        private static IEnumerable<IType> LookupUnqualifiedNameTypeInstances(
            string Name, IReadOnlyList<IType> TypeArguments, ILocalScope Scope,
            SourceLocation Location)
        {
            var ty = Scope.Function.Global.Binder.BindType(new QualifiedName(Name));
            if (ty == null)
            {
                var genericName = new SimpleName(Name, TypeArguments.Count);
                ty = Scope.Function.Global.Binder.BindType(new QualifiedName(genericName));
                if (ty == null)
                    return Enumerable.Empty<IType>();
            }
            return InstantiateTypes(new IType[] { ty }, TypeArguments, Scope.Function.Global, Location);
        }

		public static TypeOrExpression LookupUnqualifiedName(string Name, ILocalScope Scope)
		{
			var qualName = new QualifiedName(Name);
			return new TypeOrExpression(
				LookupUnqualifiedNameExpression(Name, Scope),
				LookupUnqualifiedNameTypes(qualName, Scope),
				qualName);
		}

        public static TypeOrExpression LookupUnqualifiedNameInstance(
            string Name, IReadOnlyList<IType> TypeArguments, ILocalScope Scope,
            SourceLocation Location)
        {
            return new TypeOrExpression(
                LookupUnqualifiedNameExpressionInstance(Name, TypeArguments, Scope, Location),
                LookupUnqualifiedNameTypeInstances(Name, TypeArguments, Scope, Location),
                default(QualifiedName));
        }

		public static IStatement ToStatement(IExpression Expression)
		{
			return new ExpressionStatement(Expression);
		}

		public static IExpression ToExpression(IStatement Statement)
		{
			return new InitializedExpression(Statement, VoidExpression.Instance);
		}

		public static IStatement ConvertStatement(this NodeConverter Converter, LNode Node, LocalScope Scope)
		{
			return ToStatement(Converter.ConvertExpression(Node, Scope));
		}

		public static IStatement ConvertScopedStatement(this NodeConverter Converter, LNode Node, LocalScope Scope)
		{
			var childScope = new LocalScope(Scope);
			var stmt = Converter.ConvertStatement(Node, Scope);
			return new BlockStatement(new IStatement[] { stmt, childScope.Release() });
		}

		public static IExpression ConvertScopedExpression(this NodeConverter Converter, LNode Node, LocalScope Scope)
		{
			var childScope = new LocalScope(Scope);
			var expr = Converter.ConvertExpression(Node, Scope);
			return new InitializedExpression(EmptyStatement.Instance, expr, childScope.Release());
		}

		public static IExpression ConvertExpression(
			this NodeConverter Converter, LNode Node, LocalScope Scope,
			IType Type)
		{
			return Scope.Function.Global.ConvertImplicit(
				Converter.ConvertExpression(Node, Scope), 
				Type, NodeHelpers.ToSourceLocation(Node.Range));
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
					NodeHelpers.CheckArity(item, 1, Scope.Log);

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
				if (Scope.ReturnType.IsEquivalent(PrimitiveTypes.Void)) 
				{
					return ToExpression(new ReturnStatement());
				} 
				else 
				{
					Scope.Log.LogError(new LogEntry(
						"return statement",
						NodeHelpers.HighlightEven(
							"an object of a type convertible to '",
							Scope.Function.Global.TypeNamer.Convert(Scope.ReturnType),
							"' is required for the return statement."),
						NodeHelpers.ToSourceLocation(Node.Range)));
					return ToExpression(new ReturnStatement(new UnknownExpression(Scope.ReturnType)));
				}
			}
			else
			{
				NodeHelpers.CheckArity(Node, 1, Scope.Log);

				return ToExpression(new ReturnStatement(
					Scope.Function.Global.ConvertImplicit(
						Converter.ConvertExpression(Node.Args[0], Scope), 
						Scope.ReturnType, NodeHelpers.ToSourceLocation(Node.Args[0].Range))));
			}
		}

        private static void LogGenericArityMismatch(
            IGenericMember Declaration, int ArgumentCount,
            GlobalScope Scope, SourceLocation Location)
        {
            // Invalid number of type arguments.
            Scope.Log.LogError(new LogEntry(
                "generic arity mismatch",
                NodeHelpers.HighlightEven(
                    "'", Declaration.Name.ToString(), "' takes '", Declaration.GenericParameters.Count().ToString(), 
                    "' type parameters, but was given '", ArgumentCount.ToString(), "'."),
                Location));
        }

        private static void LogCannotInstantiate(
            string MemberName, GlobalScope Scope,
            SourceLocation Location)
        {
            Scope.Log.LogError(new LogEntry(
                "syntax error",
                NodeHelpers.HighlightEven(
                    "'", MemberName, "' is not generic, and cannot be instantiated."),
                Location));
        }

        /// <summary>
        /// Checks if the given generic member can be instantiated by the given
        /// list of type arguments. A boolean that tells whether the number
        /// of generic arguments was correct, is returned. If the number of
        /// generic arguments is correct, but some constraints are not satisfied,
        /// then one or more errors are logged.
        /// </summary>
        private static bool CheckGenericConstraints(
            IGenericMember Declaration, IReadOnlyList<IType> TypeArguments,
            GlobalScope Scope, SourceLocation Location)
        {
            var genericParamArr = Declaration.GenericParameters.ToArray();

            if (genericParamArr.Length != TypeArguments.Count)
            {
                LogGenericArityMismatch(Declaration, TypeArguments.Count, Scope, Location);
                return false;
            }

            for (int i = 0; i < genericParamArr.Length; i++)
            {
                var tParam = genericParamArr[i];
                var tArg = TypeArguments[i];
                if (!tParam.Constraint.Satisfies(tArg))
                {
                    // Check that this type argument is okay for 
                    // the parameter's constraints.
                    Scope.Log.LogError(new LogEntry(
                        "generic constraint",
                        NodeHelpers.HighlightEven(
                            "type '", Scope.TypeNamer.Convert(tArg), 
                            "' does not satisfy the generic constraints on type parameter '",
                            Scope.TypeNamer.Convert(tParam), "'."),
                        Location));
                }
            }

            return true;
        }

        private static IEnumerable<IType> InstantiateTypes(
            IEnumerable<IType> Types, IReadOnlyList<IType> TypeArguments,
            GlobalScope Scope, SourceLocation Location)
        {
            return Types.Select(item => 
            {
                if (CheckGenericConstraints(item, TypeArguments, Scope, Location))
                {
                    return item.MakeGenericType(TypeArguments);
                }
                else
                {
                    LogGenericArityMismatch(item, TypeArguments.Count, Scope, Location);
                    return item.MakeGenericType(item.GenericParameters);
                }
            });
        }

        private static TypeOrExpression ConvertMemberAccess(
            TypeOrExpression Target, string MemberName,
            IReadOnlyList<IType> TypeArguments, LocalScope Scope,
            SourceLocation Location)
        {
            // First, try to resolve member-access expressions, which
            // look like this (instance member access):
            //
            //     <expr>.<identifier>
            // 
            // or like this (static member access):
            //
            //     <type>.<identifier>
            //

            IMethod method = null;

            var exprSet = new HashSet<IExpression>();

            if (Target.IsExpression)
            {
                var targetTy = Target.Expression.Type;
                foreach (var item in Scope.Function.GetInstanceMembers(targetTy, MemberName))
                {
                    CreateMemberAccess(
                        item, TypeArguments, Target.Expression, exprSet, 
                        Scope.Function.Global, Location, ref method);
                }
            }

            if (Target.IsType)
            {
                foreach (var ty in Target.Types)
                {
                    foreach (var item in Scope.Function.GetStaticMembers(ty, MemberName))
                    {
                        CreateMemberAccess(
                            item, TypeArguments, null, exprSet, 
                            Scope.Function.Global, Location, ref method);
                    }
                }
            }

            if (exprSet.Count == 0 && method != null)
            {
                // We want to provide a diagnostic here if we
                // encountered a method, but it was not given
                // the right amount of type arguments.
                LogGenericArityMismatch(method, TypeArguments.Count, Scope.Function.Global, Location);
            }

            IExpression expr = exprSet.Count > 0 
                ? IntersectionExpression.Create(exprSet)
                : null;

            // Next, we'll handle namespaces, which are 
            // really just qualified names.
            var nsName = Target.IsNamespace 
                ? new QualifiedName(MemberName).Qualify(Target.Namespace)
                : default(QualifiedName);

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
            foreach (var ty in Target.Types)
            {
                if (ty is INamespace)
                {
                    typeSet.UnionWith(((INamespace)ty).Types.Where(item => 
                    {
                        var itemName = item.Name as SimpleName;
                        return itemName.Name == MemberName 
                            && itemName.TypeParameterCount == TypeArguments.Count;
                    }));
                }
            }
            if (Target.IsNamespace)
            {
                var topLevelTy = Scope.Function.Global.Binder.BindType(nsName);
                if (topLevelTy != null)
                    typeSet.Add(topLevelTy);
            }

            return new TypeOrExpression(
                expr, 
                InstantiateTypes(
                    typeSet, TypeArguments, Scope.Function.Global, Location), 
                nsName);
        }

		/// <summary>
		/// Converts the given member access node (type @.).
		/// </summary>
		public static TypeOrExpression ConvertMemberAccess(
		    LNode Node, LocalScope Scope, NodeConverter Converter)
		{
			if (!NodeHelpers.CheckArity(Node, 2, Scope.Log))
				return TypeOrExpression.Empty;

			var target = Converter.ConvertTypeOrExpression(Node.Args[0], Scope);
            var srcLoc = NodeHelpers.ToSourceLocation(Node.Range);

			if (!Node.Args[1].IsId)
			{
				Scope.Log.LogError(new LogEntry(
					"syntax error",
					"expected an identifier on the right-hand side of a member access expression.",
                    srcLoc));
				return TypeOrExpression.Empty;
			}

			var ident = Node.Args[1].Name.Name;

            return ConvertMemberAccess(target, ident, new IType[] { }, Scope, srcLoc);
		}

        public static TypeOrExpression ConvertInstantiation(
            LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckMinArity(Node, 2, Scope.Log))
                return TypeOrExpression.Empty;

            var target = Node.Args[0];
            var args = Node.Args.Slice(1);

            // Perhaps we have encountered an array?
            int arrayDims = CodeSymbols.CountArrayDimensions(target.Name);
            if (arrayDims > 0)
            {
                NodeHelpers.CheckArity(Node, 2, Scope.Log);

                var elemTy = Converter.ConvertCheckedTypeOrError(args[0], Scope.Function.Global);
                return new TypeOrExpression(new IType[] { elemTy.MakeArrayType(arrayDims) });
            }

            // Perhaps not. How about a pointer?
            if (target.IsIdNamed(CodeSymbols._Pointer))
            {
                NodeHelpers.CheckArity(Node, 2, Scope.Log);

                var elemTy = Converter.ConvertCheckedTypeOrError(args[0], Scope.Function.Global);
                return new TypeOrExpression(new IType[] { elemTy.MakePointerType(PointerKind.TransientPointer) });
            }

            // Why, it must be a generic instance, then.

            var tArgs = args.Select(item => 
                Converter.ConvertCheckedTypeOrError(item, Scope.Function.Global)).ToArray();

            // The target of a generic instance is either
            // an unqualified expression (i.e. an Id node), 
            // or some member-access expression. (type @.)

            var srcLoc = NodeHelpers.ToSourceLocation(Node.Range);
            if (target.IsId)
            {
                return LookupUnqualifiedNameInstance(
                    target.Name.Name, tArgs, Scope, srcLoc);
            }
            else if (target.Calls(CodeSymbols.Dot))
            {
                if (!NodeHelpers.CheckArity(target, 2, Scope.Log))
                    return TypeOrExpression.Empty;

                var targetTyOrExpr = Converter.ConvertTypeOrExpression(target.Args[0], Scope);

                if (!target.Args[1].IsId)
                {
                    Scope.Log.LogError(new LogEntry(
                        "syntax error",
                        "expected an identifier on the right-hand side of a member access expression.",
                        srcLoc));
                    return TypeOrExpression.Empty;
                }

                var ident = target.Args[1].Name.Name;

                return ConvertMemberAccess(targetTyOrExpr, ident, tArgs, Scope, srcLoc);
            }
            else
            {
                Scope.Function.Global.Log.LogError(new LogEntry(
                    "syntax error",
                    "generic instantiation is only applicable to unqualified names, " +
                    "qualified names, and member-access expressions.",
                    srcLoc));
                return TypeOrExpression.Empty;
            }
        }

		/// <summary>
		/// Converts a call-expression.
		/// </summary>
		public static IExpression ConvertCall(LNode Node, LocalScope Scope, NodeConverter Converter)
		{
			var target = Converter.ConvertExpression(Node.Target, Scope).GetEssentialExpression();
			var delegates = IntersectionExpression.GetIntersectedExpressions(target);
            var args = OverloadResolution.ConvertArguments(Node.Args, Scope, Converter);

            return OverloadResolution.CreateCheckedInvocation(
                "method", delegates, args, Scope.Function.Global, 
                NodeHelpers.ToSourceLocation(Node.Range));
		}

		/// <summary>
		/// Creates a binary operator application expression
		/// for the given operator and operands. A scope is
		/// used to perform conversions and log error messages,
		/// and two source locations are used to highlight potential
		/// issues.
		/// </summary>
		public static IExpression CreateBinary(
			Operator Op, IExpression Left, IExpression Right, 
			GlobalScope Scope,
			SourceLocation LeftLocation, SourceLocation RightLocation)
		{
			var lTy = Left.Type;
			var rTy = Right.Type;

			IType opTy;
			if (BinaryOperatorResolution.TryGetPrimitiveOperatorType(Op, lTy, rTy, out opTy))
			{
				if (opTy == null)
				{
					Scope.Log.LogError(new LogEntry(
						"operator application",
						NodeHelpers.HighlightEven(
							"operator '", Op.Name, "' cannot be applied to operands of type '", 
							Scope.TypeNamer.Convert(lTy), "' and '",
							Scope.TypeNamer.Convert(rTy), "'."),
						LeftLocation.Concat(RightLocation)));
					return new UnknownExpression(lTy);
				}

				return DirectBinaryExpression.Instance.Create(
					Scope.ConvertImplicit(Left, opTy, LeftLocation), 
					Op, 
					Scope.ConvertImplicit(Right, opTy, RightLocation));
			}

			// TODO: actually implement this

			Scope.Log.LogError(new LogEntry(
				"operators not yet implemented",
				"custom binary operator resolution has not been implemented yet. Sorry. :/",
				LeftLocation.Concat(RightLocation)));

			return VoidExpression.Instance;
		}

		/// <summary>
		/// Creates a converter that analyzes binary operator nodes.
		/// </summary>
		public static Func<LNode, LocalScope, NodeConverter, IExpression> CreateBinaryOpConverter(Operator Op)
		{
			return (node, scope, conv) =>
			{
				if (!NodeHelpers.CheckArity(node, 2, scope.Log))
					return VoidExpression.Instance;

				return CreateBinary(
					Op, 
					conv.ConvertExpression(node.Args[0], scope), 
					conv.ConvertExpression(node.Args[1], scope), 
					scope.Function.Global,
					NodeHelpers.ToSourceLocation(node.Args[0].Range),
					NodeHelpers.ToSourceLocation(node.Args[1].Range));
			};
		}

        /// <summary>
        /// Determines if the given variable is a local variable.
        /// Loading a local variable has no side-effects, and 
        /// is efficient.
        /// </summary>
        public static bool IsLocalVariable(IVariable Variable)
        {
            return Variable is LocalVariableBase
                || Variable is ArgumentVariable
                || Variable is ThisVariable;
        }

		public static IExpression CreateUncheckedAssignment(
			IVariable Variable, IExpression Value)
		{
            if (IsLocalVariable(Variable))
			{
				return new InitializedExpression(
					Variable.CreateSetStatement(Value),
					Variable.CreateGetExpression());
			}
			else
			{
				var tmp = new RegisterVariable("tmp", Variable.Type);
				return new InitializedExpression(
					new BlockStatement(new IStatement[] 
					{
						tmp.CreateSetStatement(Value),
						Variable.CreateSetStatement(tmp.CreateGetExpression())
					}),
					tmp.CreateGetExpression());
			}
		}

		/// <summary>
		/// Converts an assignment node (type @=).
		/// </summary>
		public static IExpression ConvertAssignment(LNode Node, LocalScope Scope, NodeConverter Converter)
		{
			if (!NodeHelpers.CheckArity(Node, 2, Scope.Log))
				return VoidExpression.Instance;

			var lhs = Converter.ConvertExpression(Node.Args[0], Scope);
			var rhs = Converter.ConvertExpression(Node.Args[1], Scope);

			var lhsVar = AsVariable(lhs);

			if (lhsVar == null)
			{
				Scope.Log.LogError(new LogEntry(
					"malformed assignment",
					"the left-hand side of an assignment must be a variable, a property or an indexer.",
					NodeHelpers.ToSourceLocation(Node.Args[0].Range)));
				return rhs;
			}

			return CreateUncheckedAssignment(
				lhsVar, Scope.Function.Global.ConvertImplicit(
					rhs, lhsVar.Type, 
					NodeHelpers.ToSourceLocation(Node.Args[1].Range)));
		}

		/// <summary>
		/// Creates a converter that analyzes compound assignment nodes.
		/// </summary>
		public static Func<LNode, LocalScope, NodeConverter, IExpression> CreateCompoundAssignmentConverter(Operator Op)
		{
			return (node, scope, conv) =>
			{
				if (!NodeHelpers.CheckArity(node, 2, scope.Log))
					return VoidExpression.Instance;

				var lhs = conv.ConvertExpression(node.Args[0], scope);
				var rhs = conv.ConvertExpression(node.Args[1], scope);

				var lhsVar = AsVariable(lhs);

				var leftLoc = NodeHelpers.ToSourceLocation(node.Args[0].Range);
				var rightLoc = NodeHelpers.ToSourceLocation(node.Args[1].Range);

				if (lhsVar == null)
				{
					scope.Log.LogError(new LogEntry(
						"malformed assignment",
						"the left-hand side of an assignment must be a variable, a property or an indexer.",
						leftLoc));
					return rhs;
				}

				var result = CreateBinary(
					Op, lhs, rhs, scope.Function.Global, leftLoc, rightLoc);

				return CreateUncheckedAssignment(
					lhsVar, scope.Function.Global.ConvertImplicit(
						result, lhsVar.Type, rightLoc));
			};
		}

		/// <summary>
		/// Converts a variable declaration node (type #var).
		/// </summary>
		public static IExpression ConvertVariableDeclaration(LNode Node, LocalScope Scope, NodeConverter Converter)
		{
			if (!NodeHelpers.CheckMinArity(Node, 2, Scope.Log))
				return VoidExpression.Instance;

			var varTyNode = Node.Args[0];
			bool isVar = varTyNode.IsIdNamed(CodeSymbols.Missing);
			IType varTy = null;

			if (!isVar)
			{
				varTy = Converter.ConvertType(varTyNode, Scope.Function.Global);
				if (varTy == null)
				{
					Scope.Log.LogError(
						new LogEntry(
							"type resolution",
							NodeHelpers.HighlightEven("could not resolve variable type '", Node.ToString(), "'."),
							NodeHelpers.ToSourceLocation(varTyNode.Range)));
					return VoidExpression.Instance;
				}
			}

			var stmts = new List<IStatement>();
			IExpression expr = null;
			foreach (var item in Node.Args.Slice(1))
			{
                var decompNodes = NodeHelpers.DecomposeAssignOrId(item, Scope.Log);
                if (decompNodes == null)
                    continue;

				var nameNode = decompNodes.Item1;
                var val = decompNodes.Item2 == null 
                    ? null 
                    : Converter.ConvertExpression(decompNodes.Item2, Scope);

				var srcLoc = NodeHelpers.ToSourceLocation(nameNode.Range);

				if (isVar && val == null)
				{
					Scope.Log.LogError(
						new LogEntry(
							"invalid syntax",
							"an implicitly typed local variable declarator " +
							"must include an initializer.",
							srcLoc));
					continue;
				}

				string localName = nameNode.Name.Name;
				var varMember = new DescribedVariableMember(
					localName, isVar ? val.Type : varTy);
				varMember.AddAttribute(new SourceLocationAttribute(srcLoc));
				var local = Scope.DeclareLocal(localName, varMember);
				if (val != null)
				{
					stmts.Add(local.CreateSetStatement(
						Scope.Function.Global.ConvertImplicit(
                            val, varMember.VariableType, 
                            NodeHelpers.ToSourceLocation(decompNodes.Item2.Range))));
				}
				expr = local.CreateGetExpression();
			}
			return new InitializedExpression(
				new BlockStatement(stmts), expr);
		}

		/// <summary>
		/// Converts a 'this'-expression node (type #this).
		/// </summary>
		public static IExpression ConvertThisExpression(LNode Node, LocalScope Scope, NodeConverter Converter)
		{
            var thisVar = GetThisVariable(Scope);
            if (thisVar == null)
            {
                Scope.Log.LogError(new LogEntry(
                    "invalid syntax", 
                    NodeHelpers.HighlightEven(
                        "keyword '", "this", 
                        "' is not valid in a static property, static method, or static field initializer."),
                    NodeHelpers.ToSourceLocation(Node.Range)));
                return VoidExpression.Instance;
            }
            var thisExpr = thisVar.CreateGetExpression();

            if (Node.IsId)
            {
                // Regular 'this' expression.
                return thisExpr;
            }
            else
            {
                // Constructor call.
                var candidates = thisExpr.Type.GetConstructors()
                    .Where(item => !item.IsStatic)
                    .Select(item => new GetMethodExpression(item, thisExpr))
                    .ToArray();

                var args = OverloadResolution.ConvertArguments(Node.Args, Scope, Converter);

                return OverloadResolution.CreateCheckedInvocation(
                    "constructor", candidates, args, Scope.Function.Global, 
                    NodeHelpers.ToSourceLocation(Node.Range));
            }
		}

        /// <summary>
        /// Converts a 'base'-expression node (type #base).
        /// </summary>
        public static IExpression ConvertBaseExpression(LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            var thisVar = GetThisVariable(Scope);
            if (thisVar == null)
            {
                Scope.Log.LogError(new LogEntry(
                    "invalid syntax", 
                    NodeHelpers.HighlightEven(
                        "keyword '", "base", 
                        "' is not valid in a static property, static method, or static field initializer."),
                    NodeHelpers.ToSourceLocation(Node.Range)));
                return VoidExpression.Instance;
            }

            var baseType = Scope.Function.CurrentType.GetParent();
            if (baseType == null)
            {
                // This can only happen when we're compiling code for
                // a platform that doesn't have a root type.
                Scope.Log.LogError(new LogEntry(
                    "invalid syntax", 
                    NodeHelpers.HighlightEven(
                        "keyword '", "base", 
                        "' is only valid for types that have a base type."),
                    NodeHelpers.ToSourceLocation(Node.Range)));
                return VoidExpression.Instance;
            }
            
            var baseExpr = new ReinterpretCastExpression(
                thisVar.CreateGetExpression(),
                ThisVariable.GetThisType(baseType));

            if (Node.IsId)
            {
                // A 'base' expression can only occur in a 'call'
                // expression, and needs to get special treatment.
                // We can't do that here
                return baseExpr;
            }
            else
            {
                // Constructor call. This we can do.
                var candidates = baseExpr.Type.GetConstructors()
                    .Where(item => !item.IsStatic)
                    .Select(item => new GetMethodExpression(item, baseExpr))
                    .ToArray();

                var args = OverloadResolution.ConvertArguments(Node.Args, Scope, Converter);

                return OverloadResolution.CreateCheckedInvocation(
                    "constructor", candidates, args, Scope.Function.Global, 
                    NodeHelpers.ToSourceLocation(Node.Range));
            }
        }

		/// <summary>
		/// Converts an if-statement node (type #if),
		/// and wraps it in a void expression.
		/// </summary>
		public static IExpression ConvertIfExpression(LNode Node, LocalScope Scope, NodeConverter Converter)
		{
			if (!NodeHelpers.CheckArity(Node, 3, Scope.Log))
				return VoidExpression.Instance;

			var cond = Converter.ConvertExpression(Node.Args[0], Scope, PrimitiveTypes.Boolean);
			var ifExpr = Converter.ConvertScopedStatement(Node.Args[1], Scope);
			var elseExpr = Converter.ConvertScopedStatement(Node.Args[2], Scope);

			return ToExpression(new IfElseStatement(cond, ifExpr, elseExpr));
		}

		/// <summary>
		/// Converts a selection-expression. (a ternary
		/// conditional operator application)
		/// </summary>
		public static IExpression ConvertSelectExpression(LNode Node, LocalScope Scope, NodeConverter Converter)
		{
			if (!NodeHelpers.CheckArity(Node, 3, Scope.Log))
				return VoidExpression.Instance;

			var cond = Converter.ConvertExpression(Node.Args[0], Scope, PrimitiveTypes.Boolean);
			var ifExpr = Converter.ConvertScopedExpression(Node.Args[1], Scope);
			var elseExpr = Converter.ConvertScopedExpression(Node.Args[2], Scope);

			var globalScope = Scope.Function.Global;

			var ifType = ifExpr.Type;
			var elseType = elseExpr.Type;

			if (globalScope.ConversionRules.HasImplicitConversion(ifType, elseType))
			{
				ifExpr = globalScope.ConvertImplicit(
					ifExpr, elseType, NodeHelpers.ToSourceLocation(Node.Args[1].Range));
			}
			else
			{
				elseExpr = globalScope.ConvertImplicit(
					elseExpr, ifType, NodeHelpers.ToSourceLocation(Node.Args[2].Range));
			}

			return new SelectExpression(cond, ifExpr, elseExpr);
		}

		/// <summary>
		/// Converts a while-statement node (type #while),
		/// and wraps it in a void expression.
		/// </summary>
		public static IExpression ConvertWhileExpression(
            LNode Node, LocalScope Scope, NodeConverter Converter)
		{
			if (!NodeHelpers.CheckArity(Node, 2, Scope.Log))
				return VoidExpression.Instance;

			var cond = Converter.ConvertExpression(Node.Args[0], Scope, PrimitiveTypes.Boolean);
			var body = Converter.ConvertScopedStatement(Node.Args[1], Scope);

			return ToExpression(new WhileStatement(cond, body));
		}

        /// <summary>
        /// Converts a for-statement node (type #for), 
        /// and wraps it in a void expression.
        /// </summary>
        public static IExpression ConvertForExpression(
            LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckArity(Node, 4, Scope.Log))
                return VoidExpression.Instance;

            var childScope = new LocalScope(Scope);

            var init = Converter.ConvertStatement(Node.Args[0], childScope);
            var cond = Converter.ConvertExpression(Node.Args[1], childScope, PrimitiveTypes.Boolean);
            var delta = Converter.ConvertStatement(Node.Args[2], childScope);
            var body = Converter.ConvertScopedStatement(Node.Args[3], childScope);

            return ToExpression(new ForStatement(init, cond, delta, body, childScope.Release()));
        }

        /// <summary>
        /// Converts a default-value node (type #default).
        /// </summary>
        public static IExpression ConvertDefaultExpression(
            LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckArity(Node, 1, Scope.Log))
                return VoidExpression.Instance;

            var ty = Converter.ConvertCheckedType(Node.Args[0], Scope.Function.Global);

            if (ty == null)
            {
                return VoidExpression.Instance;
            }
            else if (ty.Equals(PrimitiveTypes.Void))
            {
                Scope.Log.LogError(new LogEntry(
                    "invalid syntax",
                    NodeHelpers.HighlightEven("type '", "void", "' cannot be used in this context."),
                    NodeHelpers.ToSourceLocation(Node.Args[0].Range)));
                return VoidExpression.Instance;
            }
            else
            {
                return new DefaultValueExpression(ty);
            }
        }

        /// <summary>
        /// Converts an as-instance expression node (type #as).
        /// </summary>
        public static IExpression ConvertAsInstanceExpression(
            LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckArity(Node, 2, Scope.Log))
                return VoidExpression.Instance;

            var op = Converter.ConvertExpression(Node.Args[0], Scope);
            var ty = Converter.ConvertType(Node.Args[1], Scope.Function.Global);

            // In an operation of the form E as T, E must be an expression and T must be a
            // reference type, a type parameter known to be a reference type, or a nullable type.
            if (ty.GetIsPointer())
            {
                // Spec doesn't cover pointers.
                Scope.Log.LogError(new LogEntry(
                    "invalid expression",
                    NodeHelpers.HighlightEven(
                        "the '", "as", "' operator cannot be applied to an operand of pointer type '", 
                        Scope.Function.Global.TypeNamer.Convert(ty), "'."),
                    NodeHelpers.ToSourceLocation(Node.Range)));
                return new AsInstanceExpression(op, ty);
            }
            else if (ty.GetIsStaticType())
            {
                // Make sure that we're not testing
                // against a static type.
                Scope.Log.LogError(new LogEntry(
                    "invalid expression",
                    NodeHelpers.HighlightEven(
                        "the second operand of an '", "as", 
                        "' operator cannot be '", "static", "' type '",
                        Scope.Function.Global.TypeNamer.Convert(ty), "'."),
                    NodeHelpers.ToSourceLocation(Node.Range)));
                return new AsInstanceExpression(op, ty);
            }
            else if (!ty.GetIsReferenceType())
            {
                // Oops. Try to figure out what kind of type
                // `ty` is, then.
                if (ty.GetIsGenericParameter())
                {                    
                    Scope.Log.LogError(new LogEntry(
                        "invalid expression",
                        NodeHelpers.HighlightEven(
                            "the '", "as", "' operator cannot be used with non-reference type parameter '", 
                            Scope.Function.Global.TypeNamer.Convert(ty), "'. Consider adding '", 
                            "class", "' or a reference type constraint."),
                        NodeHelpers.ToSourceLocation(Node.Range)));
                }
                else
                {
                    Scope.Log.LogError(new LogEntry(
                        "invalid expression",
                        NodeHelpers.HighlightEven(
                            "the '", "as", "' operator cannot be used with non-nullable value type '", 
                            Scope.Function.Global.TypeNamer.Convert(ty), "'."),
                        NodeHelpers.ToSourceLocation(Node.Range)));
                }
                return new AsInstanceExpression(op, ty);
            }
            else if (op.Type.Equals(PrimitiveTypes.Null))
            {
                // Early-out here, because we don't want to emit warnings
                // for things like 'null as T', because the programmer is
                // probably just trying to use that to pick a specific 
                // overload.
                // We can also use a reinterpret_cast here, because
                // we know that 'null' is convertible to the target 
                // type.
                return new ReinterpretCastExpression(op, ty);
            }

            var conv = Scope.Function.Global.ConversionRules.TryConvertExplicit(op, ty);

            if (conv == null)
            {
                Scope.Log.LogError(new LogEntry(
                    "invalid expression",
                    NodeHelpers.HighlightEven(
                        "there is no legal conversion from type '", 
                        Scope.Function.Global.TypeNamer.Convert(op.Type), "' to type '", 
                        Scope.Function.Global.TypeNamer.Convert(ty), "'."),
                    NodeHelpers.ToSourceLocation(Node.Range)));
                return new AsInstanceExpression(op, ty);
            }

            // Success! Now that we know that the expression is well-formed,
            // we also want to make sure that it's sane.
            var result = new AsInstanceExpression(op, ty);
            var evalResult = result.Evaluate();

            if (evalResult != null)
            {
                // We actually managed to evaluate this thing 
                // at compile-time. That's a warning no matter what.
                if (evalResult.Type.Equals(PrimitiveTypes.Null))
                {
                    if (EcsWarnings.AlwaysNullWarning.UseWarning(Scope.Log.Options))
                    {
                        Scope.Log.LogWarning(new LogEntry(
                            "always null",
                            EcsWarnings.AlwaysNullWarning.CreateMessage(new MarkupNode("#group", 
                                    NodeHelpers.HighlightEven(
                                        "'", "as", "' operator always evaluates to '", 
                                        "null", "' here. "))),
                            NodeHelpers.ToSourceLocation(Node.Range)));
                    }
                }
                else if (EcsWarnings.RedundantAsWarning.UseWarning(Scope.Log.Options))
                {
                    Scope.Log.LogWarning(new LogEntry(
                        "redundant 'as' operator",
                        EcsWarnings.RedundantAsWarning.CreateMessage(new MarkupNode("#group", 
                            NodeHelpers.HighlightEven(
                                "this '", "as", "' is redundant, " +
                                "and can be replaced by a cast. "))),
                        NodeHelpers.ToSourceLocation(Node.Range)));
                }
            }

            return result;
        }

        /// <summary>
        /// Converts an is-instance expression node (type #is).
        /// </summary>
        public static IExpression ConvertIsInstanceExpression(
            LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckArity(Node, 2, Scope.Log))
                return VoidExpression.Instance;

            var op = Converter.ConvertExpression(Node.Args[0], Scope);
            var ty = Converter.ConvertType(Node.Args[1], Scope.Function.Global);
            var result = new IsExpression(op, ty);

            if (ty.GetIsStaticType())
            {
                Scope.Log.LogError(new LogEntry(
                    "invalid expression",
                    NodeHelpers.HighlightEven(
                        "the second operand of an '", "is", 
                        "' operator cannot be '", "static", "' type '",
                        Scope.Function.Global.TypeNamer.Convert(ty), "'."),
                    NodeHelpers.ToSourceLocation(Node.Range)));
                return result;
            }
            var opType = op.Type;
            if (opType.GetIsPointer() || ty.GetIsPointer())
            {
                // We don't do pointer types.
                Scope.Log.LogError(new LogEntry(
                    "invalid expression",
                    NodeHelpers.HighlightEven(
                        "the '", "is", "' operator cannot be applied to an operand of pointer type '", 
                        (Scope.Function.Global.TypeNamer.Convert(opType.GetIsPointer() ? opType : ty)), "'."),
                    NodeHelpers.ToSourceLocation(Node.Range)));
                return result;
            }

            // Check that the is-instance expression doesn't
            // evaluate to some constant. If it does, then we
            // should log a warning for sure.
            var evalResult = result.Evaluate();
            if (evalResult != null)
            {
                bool resultVal = evalResult.GetPrimitiveValue<bool>();
                string lit = resultVal ? "true" : "false";
                var warn = resultVal ? EcsWarnings.AlwaysTrueWarning : EcsWarnings.AlwaysFalseWarning;
                if (warn.UseWarning(Scope.Log.Options))
                {
                    Scope.Log.LogWarning(new LogEntry(
                        "always " + lit,
                        warn.CreateMessage(new MarkupNode("#group", 
                            NodeHelpers.HighlightEven(
                                "'", "is", "' operator always evaluates to '", 
                                lit, "' here. "))),
                        NodeHelpers.ToSourceLocation(Node.Range)));
                }
            }
            else if (opType.Is(ty))
            {
                var warn = Warnings.Instance.HiddenNullCheck;
                if (warn.UseWarning(Scope.Log.Options))
                {
                    Scope.Log.LogWarning(new LogEntry(
                        "hidden null check",
                        warn.CreateMessage(new MarkupNode("#group", 
                            NodeHelpers.HighlightEven(
                                "the '", "is", "' operator is equivalent to a null check here. " +
                                "Replacing '", "is", "' by an explicit null check may be clearer. "))),
                        NodeHelpers.ToSourceLocation(Node.Range)));
                }
            }

            return result;
        }

        /// <summary>
        /// Converts a cast expression node (type #cast).
        /// </summary>
        public static IExpression ConvertCastExpression(
            LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckArity(Node, 2, Scope.Log))
                return VoidExpression.Instance;

            var op = Converter.ConvertExpression(Node.Args[0], Scope);
            var ty = Converter.ConvertType(Node.Args[1], Scope.Function.Global);

            return Scope.Function.Global.ConvertExplicit(
                op, ty, NodeHelpers.ToSourceLocation(Node.Range));
        }

        /// <summary>
        /// Converts a using-cast expression node (type #usingCast).
        /// </summary>
        public static IExpression ConvertUsingCastExpression(
            LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            // A using-cast is like a regular cast, except that
            // 
            //      x using T
            //
            // will only compile if it is guaranteed to succeed.

            if (!NodeHelpers.CheckArity(Node, 2, Scope.Log))
                return VoidExpression.Instance;

            var usingCastWarn = EcsWarnings.EcsExtensionUsingCastWarning;
            if (usingCastWarn.UseWarning(Scope.Log.Options))
            {
                Scope.Log.LogWarning(new LogEntry(
                    "EC# extension",
                    usingCastWarn.CreateMessage(
                        new MarkupNode("#group", NodeHelpers.HighlightEven(
                            "the '", "using", "' cast operator is an EC# extension. "))),
                    NodeHelpers.ToSourceLocation(Node.Range)));
            }

            var op = Converter.ConvertExpression(Node.Args[0], Scope);
            var ty = Converter.ConvertType(Node.Args[1], Scope.Function.Global);

            return Scope.Function.Global.ConvertStatic(
                op, ty, NodeHelpers.ToSourceLocation(Node.Range));
        }
	}
}

