using System;
using System.Collections.Generic;
using System.Linq;
using Flame.Build;
using Flame.Compiler;
using Flame.Compiler.Expressions;
using Flame.Compiler.Statements;
using Flame.Compiler.Variables;
using Flame.Compiler.Emit;
using Flame.Ecs.Semantics;
using Loyc;
using Loyc.Syntax;
using Pixie;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using Flame.Collections;
using Flame.Ecs.Values;

namespace Flame.Ecs
{
    /// <summary>
    /// Defines a set of members that facilitate the analysis
    /// and lowering of Enhanced C# LNodes into Flame IR.
    /// </summary>
    public static class ExpressionConverters
    {
        /// <summary>
        /// Gets the error expression, which is used whenever an error occurs.
        /// </summary>
        /// <value>The error expression.</value>
        public static IExpression ErrorTypeExpression
        {
            get { return new UnknownExpression(ErrorType.Instance); }
        }

        /// <summary>
        /// Retrieves the 'this' variable from the given
        /// local scope.
        /// </summary>
        public static IVariable GetThisVariable(ILocalScope Scope)
        {
            return Scope.GetVariable(CodeSymbols.This);
        }

        private static IValue LookupUnqualifiedNameExpression(
            Symbol Name, ILocalScope Scope)
        {
            // Early-out for local variables.
            var local = Scope.GetVariable(Name);
            if (local != null)
            {
                return new VariableValue(local);
            }

            // Create a set of potential results.
            var exprSet = new HashSet<IValue>();
            foreach (var item in Scope.Function.GetUnqualifiedStaticMembers(Name.Name))
            {
                var acc = AccessMember(null, item, Scope.Function.Global);
                if (acc != null)
                    exprSet.Add(acc);
            }

            var declType = Scope.Function.DeclaringType;

            if (declType != null)
            {
                var thisVar = GetThisVariable(Scope);
                if (thisVar != null)
                {
                    foreach (var item in Scope.Function.GetInstanceAndExtensionMembers(declType, Name.Name))
                    {
                        var acc = AccessMember(
                            new VariableValue(thisVar), 
                            item, Scope.Function.Global);
                        if (acc != null)
                            exprSet.Add(acc);
                    }
                }
            }

            return IntersectionValue.Create(exprSet);
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
            IValue TargetExpression, HashSet<IValue> Results,
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

        private static IValue LookupUnqualifiedNameExpressionInstance(
            string Name, IReadOnlyList<IType> TypeArguments, ILocalScope Scope,
            SourceLocation Location)
        {
            IMethod method = null;

            // Create a set of potential results.
            var exprSet = new HashSet<IValue>();
            foreach (var item in Scope.Function.GetUnqualifiedStaticMembers(Name))
            {
                CreateMemberAccess(
                    item, TypeArguments, null, exprSet,
                    Scope.Function.Global, Location, ref method);
            }

            var declType = Scope.Function.DeclaringType;

            if (declType != null)
            {
                var thisVar = GetThisVariable(Scope);
                if (GetThisVariable(Scope) != null)
                {
                    foreach (var item in Scope.Function.GetInstanceAndExtensionMembers(declType, Name))
                    {
                        CreateMemberAccess(
                            item, TypeArguments, new ExpressionValue(thisVar.CreateGetExpression()), exprSet,
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
                // Add the error-type expression to the set, to
                // keep the node converter from logging a diagnostic
                // about the fact that the expression returned here is
                // not a value.
                exprSet.Add(new ExpressionValue(ErrorTypeExpression));
            }

            return IntersectionValue.Create(exprSet);
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
            var genericName = new SimpleName(Name, TypeArguments.Count);
            var ty = Scope.Function.Global.Binder.BindType(new QualifiedName(genericName));
            if (ty == null)
            {
                return Enumerable.Empty<IType>();
            }
            else
            {
                return InstantiateTypes(new IType[] { ty }, TypeArguments, Scope.Function.Global, Location);
            }
        }

        public static TypeOrExpression LookupUnqualifiedName(Symbol Name, ILocalScope Scope)
        {
            var qualName = new QualifiedName(Name.Name);
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
            if (Expression is AssignmentExpression)
            {
                return ((AssignmentExpression)Expression).ToStatement();
            }
            else if (Expression is InitializedExpression)
            {
                var initExpr = (InitializedExpression)Expression;
                return new BlockStatement(new IStatement[]
                    {
                        initExpr.Initialization,
                        ToStatement(initExpr.Value),
                        initExpr.Finalization
                    });
            }
            else if (Expression is SourceExpression)
            {
                var srcExpr = (SourceExpression)Expression;
                return SourceStatement.Create(
                    ToStatement(srcExpr.Value), srcExpr.Location);
            }
            else
            {
                return new ExpressionStatement(Expression);
            }
        }

        public static IExpression ToExpression(IStatement Statement)
        {
            return new InitializedExpression(Statement, VoidExpression.Instance);
        }

        public static IStatement ConvertStatement(this NodeConverter Converter, LNode Node, LocalScope Scope)
        {
            return ToStatement(Converter.ConvertExpression(Node, Scope));
        }

        public static IStatement ConvertStatementBlock(this NodeConverter Converter, IEnumerable<LNode> Nodes, LocalScope Scope)
        {
            return new BlockStatement(Nodes.Select(n => Converter.ConvertStatement(n, Scope)).ToArray());
        }

        public static IStatement ConvertScopedStatement(this NodeConverter Converter, LNode Node, ILocalScope Scope)
        {
            var childScope = new LocalScope(Scope);
            var stmt = Converter.ConvertStatement(Node, childScope);
            return new BlockStatement(new IStatement[] { stmt, childScope.Release() });
        }

        public static IExpression ConvertScopedExpression(this NodeConverter Converter, LNode Node, ILocalScope Scope)
        {
            var childScope = new LocalScope(Scope);
            var expr = Converter.ConvertExpression(Node, childScope);
            return new InitializedExpression(EmptyStatement.Instance, expr, childScope.Release());
        }

        public static TypeOrExpression ConvertScopedTypeOrExpression(this NodeConverter Converter, LNode Node, ILocalScope Scope)
        {
            var childScope = new LocalScope(Scope);
            var expr = Converter.ConvertTypeOrExpression(Node, childScope);
            if (expr.IsExpression)
                return new TypeOrExpression(
                    new ScopedValue(expr.Expression, childScope),
                    expr.Types, expr.Namespace);
            else
                return expr;
        }

        public static IExpression ConvertExpression(
            this NodeConverter Converter, LNode Node, LocalScope Scope,
            IType Type)
        {
            return Scope.Function.ConvertImplicit(
                Converter.ConvertExpression(Node, Scope),
                Type, NodeHelpers.ToSourceLocation(Node.Range));
        }

        /// <summary>
        /// Creates an expression that represents an address to
        /// a storage location that contains the given expression.
        /// If this expression is a variable, then an address to said
        /// variable is returned. Otherwise, a temporary is created,
        /// and said temporary's address is returned.
        /// </summary>
        public static ResultOrError<IExpression, LogEntry> ToValueAddress(
            IValue Value, ILocalScope Scope, SourceLocation Location)
        {
            var addr = Value.CreateAddressOfExpression(Scope, Location);
            if (!addr.IsError)
                return addr;

            return Value.CreateGetExpression(Scope, Location)
                .MapResult(ToTemporaryAddress);
        }

        /// <summary>
        /// Builds an IR tree that creates a temporary, assigns
        /// the given value to it, and returns the temporary's address.
        /// </summary>
        /// <returns>The temporary variable's address.</returns>
        /// <param name="Value">The value of the temporary value.</param>
        public static IExpression ToTemporaryAddress(
            IExpression Value)
        {
            var temp = new LocalVariable("tmp", Value.Type);
            return new InitializedExpression(
                temp.CreateSetStatement(Value),
                temp.CreateAddressOfExpression());
        }

        /// <summary>
        /// Creates a value that can be used
        /// as the target object for a member-access expression.
        /// </summary>
        public static ResultOrError<IExpression, LogEntry> AsTargetValue(
            IValue Value, ILocalScope Scope, 
            SourceLocation Location, bool CreateTemporary)
        {
            if (Value == null)
                return ResultOrError<IExpression, LogEntry>.FromResult(null);
            else if (Value.Type.GetIsReferenceType())
                return Value.CreateGetExpression(Scope, Location);
            else if (CreateTemporary)
                return ToValueAddress(Value, Scope, Location);
            else
                return Value.CreateAddressOfExpression(Scope, Location);
        }

        /// <summary>
        /// Creates an expression that can be used
        /// as the target object for a member-access expression.
        /// If an address is required, then a temporary will be 
        /// created, and its address will be returned.
        /// </summary>
        public static IExpression AsTargetExpression(
            IExpression Value)
        {
            if (Value == null)
                return null;
            else if (Value.Type.GetIsReferenceType())
                return Value;
            else
                return ToTemporaryAddress(Value);
        }

        /// <summary>
        /// Appends a `return(void);` statement to the given function
        /// body expression, provided the return type is either `null`
        /// or `void`. Otherwise, the body expression's value is returned,
        /// provided that its return value is not 'void'.
        /// </summary>
        public static IStatement AutoReturn(IType ReturnType, IExpression Body, SourceLocation Location, FunctionScope Scope)
        {
            if (ReturnType == null || ReturnType.Equals(PrimitiveTypes.Void))
                return new BlockStatement(new[] { ToStatement(Body), new ReturnStatement() });
            else if (!Body.Type.Equals(PrimitiveTypes.Void))
                return new ReturnStatement(Scope.ConvertImplicit(Body, ReturnType, Location));
            else
                return ToStatement(Body);
        }

        /// <summary>
        /// Accesses the given type member on the given target
        /// expression.
        /// </summary>
        public static IValue AccessMember(
            IValue Target, ITypeMember Member, GlobalScope Scope)
        {
            if (Member is IField)
            {
                return new FieldValue((IField)Member, Target);
            }
            else if (Member is IProperty)
            {
                // Indexers are special, and shouldn't be handled
                // here.
                var prop = (IProperty)Member;
                if (prop.GetIsIndexer())
                    return null;

                return new PropertyValue(prop, Target);
            }
            else if (Member is IMethod)
            {
                var method = (IMethod)Member;
                return new ComputedExpressionValue(
                    MethodType.Create(method),
                    (scope, srcLoc) =>
                    {
                        if (Member.GetIsExtension() && Target != null)
                        {
                            return Target.CreateGetExpression(scope, srcLoc)
                                .MapResult<IExpression>(targetExpr =>
                                    new GetExtensionMethodExpression(
                                        method, targetExpr));
                        }
                        else
                        {
                            return AsTargetValue(Target, scope, srcLoc, true)
                                .MapResult<IExpression>(targetExpr =>
                                    {
                                        var usingBoxExpr = targetExpr.GetEssentialExpression() as UsingBoxExpression;
                                        if (usingBoxExpr != null)
                                        {
                                            // Log a warning whenever we elide a boxing conversion, because
                                            // regular C# typically can't do that.
                                            var usingCastWarn = EcsWarnings.EcsExtensionUsingCastWarning;
                                            if (Scope.UseWarning(usingCastWarn))
                                            {
                                                Scope.Log.LogWarning(new LogEntry(
                                                    "EC# extension",
                                                    usingCastWarn.CreateMessage(
                                                        new MarkupNode("#group", NodeHelpers.HighlightEven(
                                                            "this usage of '", "using", "' cannot be translated " +
                                                            "faithfully to C#, because a cast requires boxing but '",
                                                            "using", "' does not. "))),
                                                    srcLoc));
                                            }

                                            targetExpr = usingBoxExpr.Value;
                                        }

                                        return new GetMethodExpression(method, targetExpr);
                                    });
                        }
                    });
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
                    Scope.Function.ConvertImplicit(
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
                // Don't log generic arity mismatches here. They can be logged
                // elsewhere, and the return value tells the caller whether
                // a match has occurred or not.
                return false;
            }

            // First, build a dictionary that maps type parameters
            // to type arguments.
            var tMap = new Dictionary<IType, IType>();
            for (int i = 0; i < genericParamArr.Length; i++)
            {
                var tParam = genericParamArr[i];
                var tArg = TypeArguments[i];
                tMap[tParam] = tArg;
            }
            // Create a type mapping converter to convert generic
            // parameters to arguments.
            var conv = new TypeMappingConverter(tMap);

            // Then iterate over the type parameters, and check that
            // their constraints are satisfied.
            for (int i = 0; i < genericParamArr.Length; i++)
            {
                var tParam = genericParamArr[i];
                var tArg = TypeArguments[i];
                if (!tParam.Constraint.Transform(conv).Satisfies(tArg))
                {
                    // Check that this type argument is okay for
                    // the parameter's (transformed) constraints.
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

            var exprSet = new HashSet<IValue>();

            if (Target.IsExpression)
            {
                var targetTy = Target.Expression.Type;
                foreach (var item in Scope.Function.GetInstanceAndExtensionMembers(targetTy, MemberName))
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
                // Add the error-type expression to the set, to
                // keep the node converter from logging a diagnostic
                // about the fact that the expression returned here is
                // not a value.
                exprSet.Add(new ExpressionValue(ErrorTypeExpression));
            }

            var expr = IntersectionValue.Create(exprSet);

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

            var rhs = Node.Args[1];

            string ident;
            IType[] tArgs;
            if (rhs.CallsMin(CodeSymbols.Of, 1))
            {
                ident = rhs.Args[0].Name.Name;
                tArgs = rhs.Slice(1).Select(item =>
                    Converter.ConvertCheckedTypeOrError(item, Scope)).ToArray();
            }
            else if (rhs.IsId)
            {
                ident = rhs.Name.Name;
                tArgs = new IType[] { };
            }
            else
            {
                Scope.Log.LogError(new LogEntry(
                    "syntax error",
                    "expected an identifier or a generic instantiation " +
                    "on the right-hand side of a member access expression.",
                    srcLoc));
                return TypeOrExpression.Empty;
            }

            return ConvertMemberAccess(target, ident, tArgs, Scope, srcLoc);
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

                var elemTy = Converter.ConvertCheckedTypeOrError(args[0], Scope);
                return new TypeOrExpression(new IType[] { elemTy.MakeArrayType(arrayDims) });
            }

            // Perhaps not. How about a pointer?
            if (target.IsIdNamed(CodeSymbols._Pointer))
            {
                NodeHelpers.CheckArity(Node, 2, Scope.Log);

                var elemTy = Converter.ConvertCheckedTypeOrError(args[0], Scope);
                return new TypeOrExpression(new IType[] { elemTy.MakePointerType(PointerKind.TransientPointer) });
            }

            // Why, it must be a generic instance, then.

            var tArgs = args.Select(item =>
                Converter.ConvertCheckedTypeOrError(item, Scope)).ToArray();

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
                "method", delegates, args, Scope.Function,
                NodeHelpers.ToSourceLocation(Node.Range));
        }

        /// <summary>
        /// Converts a new-expression. (type #new)
        /// </summary>
        public static IExpression ConvertNewExpression(LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            // The given C# new-expressions:
            //
            //     new T(args...) { values... }
            //     new T[args...] { values... }
            //     new[] { values... }
            //     new { values... }
            //
            // are converted to the given Loyc trees:
            //
            //     #new(T(args...), values...)
            //     #new(#of(@`[]`, T)(args...), values...)
            //     #new(@`[]`, values...)
            //     #new(@``, values...)
            //

            if (!NodeHelpers.CheckMinArity(Node, 1, Scope.Log))
                return ErrorTypeExpression;

            var ctorCallNode = Node.Args[0];

            var loc = NodeHelpers.ToSourceLocation(Node.Range);
            var funScope = Scope.Function;
            var initializerList = Node.Args.Slice(1);

            if (ctorCallNode.IsIdNamed(GSymbol.Empty))
            {
                return ConvertNewAnonymousTypeInstance(initializerList, loc, Scope, Converter);
            }

            NodeHelpers.CheckCall(ctorCallNode, Scope.Log);

            if (ctorCallNode.Target.IsIdNamed(CodeSymbols.Array))
            {
                // Type-inferred array type.
                var elems = OverloadResolution.ConvertArguments(initializerList, Scope, Converter);

                var elemType = TypeInference.GetBestType(elems.Select(item => item.Item1.Type), funScope);

                if (elemType == null)
                {
                    Scope.Log.LogError(new LogEntry(
                        "type inference",
                        NodeHelpers.HighlightEven(
                            "could not infer an element type for a " +
                            "type-inferred initialized arrays."),
                        loc));
                    return ErrorTypeExpression;
                }
                else
                {
                    return new InitializedArrayExpression(
                        elemType, elems.Select(expr =>
                            funScope.ConvertImplicit(
                                expr.Item1, elemType, expr.Item2)).ToArray());
                }
            }

            var ctorType = Converter.ConvertCheckedType(ctorCallNode.Target, Scope);
            var ctorArgs = OverloadResolution.ConvertArguments(ctorCallNode.Args, Scope, Converter);

            if (ctorType == null)
                return ErrorTypeExpression;

            if (ctorType.GetIsArray())
            {
                var arrTy = ctorType.AsArrayType();
                var initializerExprs = initializerList
                    .Select(n =>
                        funScope.ConvertImplicit(
                            Converter.ConvertExpression(n, Scope),
                            arrTy.ElementType,
                            loc))
                    .ToArray();
                var arrDims = ctorArgs
                    .Select(item =>
                        funScope.ConvertImplicit(
                            item.Item1, PrimitiveTypes.Int32, item.Item2))
                    .ToArray();

                if (arrDims.Length == 0)
                {
                    // Stuff that looks like: new T[] { values... }
                    if (initializerExprs.Length == 0)
                    {
                        // Syntax error.
                        Scope.Log.LogError(new LogEntry(
                            "syntax error",
                            NodeHelpers.HighlightEven(
                                "array creation must have array size or array initializer."),
                            loc));
                        return new UnknownExpression(ctorType);
                    }
                    else if (arrTy.ArrayRank == 1)
                    {
                        // This is actually pretty easy. Just
                        // create a new-array expression.
                        return new InitializedArrayExpression(
                            arrTy.ElementType, initializerExprs);
                    }
                    else
                    {
                        // TODO: implement this at some point.
                        Scope.Log.LogError(new LogEntry(
                            "array creation",
                            NodeHelpers.HighlightEven(
                                "initialized array creation for array ranks greater than '",
                                "1", "' has not been implemented yet."),
                            loc));
                        return new UnknownExpression(ctorType);
                    }
                }
                else
                {
                    // Stuff that looks like: new T[args...] { values... }
                    if (initializerExprs.Length == 0)
                    {
                        // Nothing can go wrong here, so this is pretty easy.
                        return new NewArrayExpression(
                            arrTy.ElementType, arrDims);
                    }
                    else if (arrTy.ArrayRank == 1)
                    {
                        // This is doable. We ought to check for
                        // size mismatches, though.
                        int expectedSize = initializerExprs.Length;
                        var dim = arrDims[0];
                        if (!dim.EvaluatesTo(new IntegerValue(expectedSize)))
                        {
                            Scope.Log.LogError(new LogEntry(
                                "array creation",
                                NodeHelpers.HighlightEven(
                                    "expected a constant size '",
                                    expectedSize.ToString(),
                                    "' for this initialized array."),
                                loc));
                        }

                        return new InitializedArrayExpression(
                            arrTy.ElementType, initializerExprs);
                    }
                    else
                    {
                        // TODO: implement this at some point.
                        Scope.Log.LogError(new LogEntry(
                            "array creation",
                            NodeHelpers.HighlightEven(
                                "initialized array creation for array ranks greater than '",
                                "1", "' has not been implemented yet."),
                            loc));
                        return new UnknownExpression(ctorType);
                    }
                }
            }
            else if (ctorType.GetIsPointer())
            {
                Scope.Log.LogError(new LogEntry(
                    "object creation",
                    NodeHelpers.HighlightEven(
                        "pointer type '", Scope.Function.Global.TypeNamer.Convert(ctorType),
                        "' cannot be used in an object creation expression."),
                    loc));
                return new UnknownExpression(ctorType);
            }
            else if (ctorType.GetIsStaticType())
            {
                Scope.Log.LogError(new LogEntry(
                    "object creation",
                    NodeHelpers.HighlightEven(
                        "cannot create an instance of static type '",
                        Scope.Function.Global.TypeNamer.Convert(ctorType),
                        "'."),
                    loc));
                return new UnknownExpression(ctorType);
            }
            else if (ctorType.GetIsGenericParameter())
            {
                // TODO: implement the `T : new()` constraint
                Scope.Log.LogError(new LogEntry(
                    "object creation",
                    NodeHelpers.HighlightEven(
                        "cannot create an instance of generic parameter type '",
                        Scope.Function.Global.TypeNamer.Convert(ctorType),
                        "'."),
                    loc));
                return new UnknownExpression(ctorType);
            }
            else
            {
                var newInstExpr = OverloadResolution.CreateCheckedNewObject(
                    Scope.Function.GetInstanceConstructors(ctorType),
                    ctorArgs, Scope.Function, loc);

                if (initializerList.Count == 0)
                    // Empty initializer list. Easy.
                    return newInstExpr;

                // We have a non-empty initializer list.
                var constructedObjTy = newInstExpr.Type;
                var tmp = new LocalVariable("tmp", constructedObjTy);
                var constructedObjExpr = new VariableValue(tmp);
                var addDelegates = new Lazy<IExpression[]>(() =>
                    Scope.Function.GetInstanceAndExtensionMembers(constructedObjTy, "Add")
                    .OfType<IMethod>()
                    .Select(m => 
                        AccessMember(constructedObjExpr, m, Scope.Function.Global)
                        .CreateGetExpression(Scope, loc)
                        .ResultOrDefault)
                    .Where(expr => expr != null)
                    .ToArray());

                var initMembers = new HashSet<string>();
                var init = new IStatement[]
                    {
                        tmp.CreateSetStatement(newInstExpr),
                    }.Concat(initializerList.Select(n =>
                        ConvertInitializerNode(
                            constructedObjExpr, constructedObjTy,
                            addDelegates, n, Scope, Converter,
                            initMembers)));

                return new InitializedExpression(
                    new BlockStatement(init.ToArray()),
                    tmp.CreateGetExpression(),
                    tmp.CreateReleaseStatement());
            }
        }

        // A table that counts the number of anonymous types per type.
        // This is used to generate unique names for anonymous types.
        private static readonly ConditionalWeakTable<IType, Box<int>> anonymousTypeCounts =
            new ConditionalWeakTable<IType, Box<int>>();

        private static IExpression ConvertNewAnonymousTypeInstance(
            IEnumerable<LNode> InitializerList, SourceLocation Location,
            LocalScope Scope, NodeConverter Converter)
        {
            var curType = Scope.Function.CurrentType;
            Debug.Assert(curType != null);
            if (curType.GetIsPointer())
                curType = curType.AsPointerType().ElementType;

            var declTy = curType.GetRecursiveGenericDeclaration();
            Debug.Assert(declTy != null);
            Debug.Assert(declTy is INamespace);

            var curMethod = Scope.Function.CurrentMethod;
            // It's okay for the enclosing method to be null.
            var genericParams = curMethod == null
                ? new IGenericParameter[0]
                : curMethod.GenericParameters.ToArray();

            // Get the anonymous type index for the enclosing type,
            // and increment the counter.
            var tyBox = anonymousTypeCounts.GetValue(declTy, _ => new Box<int>(0));
            int tyIndex;
            lock (tyBox)
            {
                tyIndex = tyBox.Value;
                tyBox.Value = tyIndex + 1;
            }

            // Create a new type to instantiate.
            var anonTy = new DescribedType(
                new SimpleName(
                    "__anonymous_type$" + tyIndex, genericParams.Length),
                (INamespace)declTy);
            foreach (var tParam in GenericExtensions.CloneGenericParameters(genericParams, anonTy))
            {
                anonTy.AddGenericParameter(tParam);
            }
            var genericParamConv = new TypeParameterConverter(anonTy);

            // Mark the anonymous type as a private reference type.
            anonTy.AddAttribute(new AccessAttribute(AccessModifier.Private));
            anonTy.AddAttribute(PrimitiveAttributes.Instance.ReferenceTypeAttribute);
            // Throw in a source location, as well.
            anonTy.AddAttribute(new SourceLocationAttribute(Location));

            // Add a base type if that's appropriate.
            var rootTy = Scope.Function.Global.Binder.Environment.RootType;
            if (rootTy != null)
                anonTy.AddBaseType(rootTy);

            // Synthesize a constructor.
            var ctor = new DescribedBodyMethod("this", anonTy);
            ctor.IsConstructor = true;
            ctor.IsStatic = false;
            ctor.AddAttribute(new AccessAttribute(AccessModifier.Public));
            ctor.ReturnType = PrimitiveTypes.Void;

            if (rootTy == null)
            {
                ctor.Body = new ReturnStatement();
            }
            else
            {
                var baseCtor = rootTy.GetConstructor(new IType[] { }, false);
                if (baseCtor == null)
                {
                    // This means that the back-end is doing something
                    // highly unorthodox.
                    Scope.Log.LogError(new LogEntry(
                        "missing root constructor",
                        NodeHelpers.HighlightEven(
                            "could not create an anonymous type, because " +
                            "root type '", Scope.Function.Global.TypeNamer.Convert(rootTy),
                            "' does not have a parameterless constructor."),
                        Location));
                    ctor.Body = new ReturnStatement();
                }
                else
                {
                    // A generic instance of the anonymous type, instantiated with
                    // its own generic parameters.
                    var anonGenericTyInst = (curType.GetIsRecursiveGenericInstance()
                        ? new GenericInstanceType(anonTy, ((GenericTypeBase)curType).Resolver, curType)
                        : (IType)anonTy).MakeGenericType(anonTy.GenericParameters);

                    // Call the root type's parameterless constructor in the
                    // anonymous type's (parameterless) constructor.
                    var anonThisExpr = new ThisVariable(anonGenericTyInst).CreateGetExpression();
                    ctor.Body = new BlockStatement(new IStatement[]
                        {
                            new ExpressionStatement(new InvocationExpression(
                                baseCtor, anonThisExpr,
                                Enumerable.Empty<IExpression>())),
                            new ReturnStatement()
                        });
                }
            }
            anonTy.AddMethod(ctor);

            // A generic instance of the anonymous type, instantiated with
            // the generic parameters of the enclosing method, if any.
            var anonMethodTyInst = (curType.GetIsRecursiveGenericInstance()
                ? new GenericInstanceType(anonTy, ((GenericTypeBase)curType).Resolver, curType)
                : (IType)anonTy).MakeGenericType(genericParams);

            var anonTyVar = new LocalVariable("tmp", anonMethodTyInst);
            var anonTyVal = anonTyVar.CreateGetExpression();

            var initStmts = new List<IStatement>();
            initStmts.Add(anonTyVar.CreateSetStatement(
                new NewObjectExpression(
                    anonMethodTyInst.GetConstructor(new IType[0], false),
                    Enumerable.Empty<IExpression>())));
            var fieldDecls = new Dictionary<string, IField>();
            foreach (var node in InitializerList)
            {
                string fieldName;
                IExpression val;
                var loc = NodeHelpers.ToSourceLocation(node.Range);
                if (node.IsId)
                {
                    fieldName = node.Name.Name;
                    val = Converter.ConvertExpression(node, Scope);
                }
                else if (node.Calls(CodeSymbols.Assign))
                {
                    if (!NodeHelpers.CheckArity(node, 2, Scope.Log))
                        continue;

                    val = Converter.ConvertExpression(node.Args[1], Scope);
                    if (!NodeHelpers.CheckId(node.Args[0], Scope.Log))
                        continue;

                    fieldName = node.Args[0].Name.Name;
                }
                else if (node.Calls(CodeSymbols.Dot))
                {
                    if (!NodeHelpers.CheckArity(node, 2, Scope.Log))
                        continue;

                    val = Converter.ConvertExpression(node, Scope);
                    if (!NodeHelpers.CheckId(node.Args[1], Scope.Log))
                        continue;

                    fieldName = node.Args[1].Name.Name;
                }
                else
                {
                    Scope.Log.LogError(new LogEntry(
                        "syntax error",
                        "invalid anonymous type member declarator: " +
                        "anonymous type members must be a member assignment, " +
                        "simple name or member access expression.",
                        loc));
                    continue;
                }

                if (fieldDecls.ContainsKey(fieldName))
                {
                    Scope.Log.LogError(new LogEntry(
                        "member redefinition",
                        NodeHelpers.HighlightEven(
                            "anonymous type member '", fieldName,
                            "' is declared more than once.").Concat(new MarkupNode[]
                                {
                                    loc.CreateDiagnosticsNode(),
                                    fieldDecls[fieldName].GetSourceLocation()
                                        .CreateRemarkDiagnosticsNode("previous declaration: ")
                                    })));
                    continue;
                }

                var field = new DescribedField(
                    new SimpleName(fieldName), anonTy,
                    genericParamConv.Convert(val.Type));
                field.IsStatic = false;
                field.AddAttribute(new AccessAttribute(AccessModifier.Public));
                anonTy.AddField(field);
                fieldDecls[fieldName] = field;
                if (anonMethodTyInst.GetIsRecursiveGenericInstance())
                {
                    var genericInst = (GenericTypeBase)anonMethodTyInst;
                    initStmts.Add(new FieldVariable(
                        new GenericInstanceField(field, genericInst.Resolver, genericInst),
                        anonTyVal).CreateSetStatement(val));
                }
                else
                {
                    initStmts.Add(new FieldVariable(field, anonTyVal).CreateSetStatement(val));
                }
            }

            return new InitializedExpression(
                new BlockStatement(initStmts),
                anonTyVal,
                anonTyVar.CreateReleaseStatement());
        }

        /// <summary>
        /// Converts the given initializer list element.
        /// </summary>
        private static IStatement ConvertInitializerNode(
            IValue ConstructedObject, IType NewObjectType,
            Lazy<IExpression[]> LazyAddDelegates,
            LNode Node, LocalScope Scope, NodeConverter Converter,
            HashSet<string> InitializedMembers)
        {
            var loc = NodeHelpers.ToSourceLocation(Node.Range);
            if (Node.Calls(CodeSymbols.Assign))
            {
                // Object initializer.
                if (!NodeHelpers.CheckArity(Node, 2, Scope.Log))
                    return EmptyStatement.Instance;

                var val = Converter.ConvertExpression(Node.Args[1], Scope);
                if (!NodeHelpers.CheckId(Node.Args[0], Scope.Log))
                    return new ExpressionStatement(val);

                string fieldName = Node.Args[0].Name.Name;
                var members = Scope.Function.GetInstanceMembers(NewObjectType, fieldName)
                    .Where(m => m is IField || m is IProperty)
                    .ToArray();

                if (members.Length == 0)
                {
                    Scope.Log.LogError(new LogEntry(
                        "initializer list",
                        NodeHelpers.HighlightEven(
                            "could not resolve initialized member '", fieldName, "'."),
                        loc));
                    return new ExpressionStatement(val);
                }
                else if (members.Length > 1)
                {
                    Scope.Log.LogError(new LogEntry(
                        "initializer list",
                        NodeHelpers.HighlightEven(
                            "initialized member '", fieldName, "' was ambiguous in this context."),
                        loc));
                    return new ExpressionStatement(val);
                }
                else
                {
                    var memberVal = AccessMember(ConstructedObject, members[0], Scope.Function.Global);

                    if (memberVal == null)
                    {
                        Scope.Log.LogError(new LogEntry(
                            "initializer list",
                            NodeHelpers.HighlightEven(
                                "could not assign a value to member '", fieldName, "'."),
                            loc));
                        return new ExpressionStatement(val);
                    }

                    // Only check for duplicate initialization if nothing
                    // went wrong. We don't want to pollute the log
                    // with warnings when we have actual errors to worry
                    // about.
                    if (!InitializedMembers.Add(fieldName)
                        && Scope.Function.Global.UseWarning(EcsWarnings.DuplicateInitializationWarning))
                    {
                        Scope.Log.LogWarning(new LogEntry(
                            "duplicate initialization",
                            NodeHelpers.HighlightEven(
                                "object initializer contains more than one member '",
                                fieldName, "' initialization. ")
                            .Concat(new[] { EcsWarnings.DuplicateInitializationWarning.CauseNode }),
                            loc));
                    }

                    return ToStatement(CreateCheckedAssignment(
                        memberVal, val, Scope, loc));
                }
            }
            else
            {
                // Collection initializer.
                var args = Node.Calls(CodeSymbols.Braces)
                    ? OverloadResolution.ConvertArguments(Node.Args, Scope, Converter)
                    : new Tuple<IExpression, SourceLocation>[]
                {
                    Tuple.Create(
                        Converter.ConvertExpression(Node, Scope), loc)
                };

                return new ExpressionStatement(
                    OverloadResolution.CreateCheckedInvocation(
                        "initializer", LazyAddDelegates.Value,
                        args, Scope.Function, loc));
            }
        }

        /// <summary>
        /// Creates an expression that converts the value returned
        /// by the given expression to a string.
        /// </summary>
        private static IExpression ValueToString(
            IValue Value, ILocalScope Scope,
            SourceLocation Location)
        {
            if (PrimitiveTypes.String.Equals(Value.Type))
                return Value.CreateGetExpressionOrError(Scope, Location);

            var fScope = Scope.Function;
            var targetVal = AsTargetValue(Value, Scope, Location, true);
            if (targetVal.IsError)
            {
                fScope.Global.Log.LogError(targetVal.Error);
                return new UnknownExpression(PrimitiveTypes.String);
            }

            var valTy = Value.Type;
            var toStringMethods =
                fScope.GetInstanceMembers(valTy, "ToString")
                    .OfType<IMethod>()
                    .Where(item =>
                        !item.Parameters.Any()
                        && !item.GenericParameters.Any()
                        && PrimitiveTypes.String.Equals(item.ReturnType))
                    .ToArray();

            // The existence of a 'ToString' method is dependent on
            // the back-end.
            if (toStringMethods.Length == 0)
            {
                fScope.Global.Log.LogError(new LogEntry(
                    "missing conversion",
                    NodeHelpers.HighlightEven(
                        "value of type '", fScope.Global.TypeNamer.Convert(valTy),
                        "' cannot not be converted to type '",
                        fScope.Global.TypeNamer.Convert(PrimitiveTypes.String),
                        "', because it does not have a parameterless, non-generic '",
                        "ToString", "' method that returns a '",
                        fScope.Global.TypeNamer.Convert(PrimitiveTypes.String),
                        "' instance."),
                    Location));
                return new UnknownExpression(PrimitiveTypes.String);
            }
            else if (toStringMethods.Length > 1)
            {
                // This shouldn't happen, but we should check for it anyway.
                fScope.Global.Log.LogError(new LogEntry(
                    "missing conversion",
                    NodeHelpers.HighlightEven(
                        "value of type '", fScope.Global.TypeNamer.Convert(valTy),
                        "' cannot not be converted to type '",
                        fScope.Global.TypeNamer.Convert(PrimitiveTypes.String),
                        "', there is more than one parameterless, non-generic '",
                        "ToString", "' method that returns a '",
                        fScope.Global.TypeNamer.Convert(PrimitiveTypes.String),
                        "' instance."),
                    Location));
                return new UnknownExpression(PrimitiveTypes.String);
            }

            return new InvocationExpression(
                toStringMethods[0], 
                targetVal.Result, 
                new IExpression[0]);
        }

        private static IExpression TryConvertEnumOperand(
            IExpression Value, IType UnderlyingType,
            FunctionScope Scope)
        {
            var valTy = Value.Type;
            if (valTy.GetIsEnum() && valTy.GetParent().Equals(UnderlyingType))
            {
                return new StaticCastExpression(Value, UnderlyingType);
            }
            else
            {
                var implConv = Scope
                    .ClassifyConversion(Value, UnderlyingType)
                    .Where(x => x.IsImplicit && x.IsStatic)
                    .ToArray();

                if (implConv.Length == 1)
                    return implConv[0].Convert(Value, UnderlyingType);
                else
                    return null;
            }
        }

        /// <summary>
        /// Creates a binary operator application expression
        /// for the given operator and operands. A scope is
        /// used to perform conversions and log error messages,
        /// and two source locations are used to highlight potential
        /// issues.
        /// </summary>
        public static IExpression CreateBinary(
            Operator Op, IValue Left, IValue Right,
            FunctionScope Scope,
            SourceLocation LeftLocation, SourceLocation RightLocation)
        {
            var lTy = Left.Type;
            var rTy = Right.Type;

            var globalScope = Scope.Global;

            // Concatenation
            if (Op.Equals(Operator.Add)
                && (PrimitiveTypes.String.Equals(lTy) || PrimitiveTypes.String.Equals(rTy)))
            {
                return new ConcatExpression(new IExpression[]
                    {
                        ValueToString(Left, Scope, LeftLocation),
                        ValueToString(Right, Scope, RightLocation)
                    });
            }

            var lExpr = Left.CreateGetExpressionOrError(Scope, LeftLocation);
            var rExpr = Right.CreateGetExpressionOrError(Scope, RightLocation);

            // Primitive operators
            IType opTy;
            if (BinaryOperatorResolution.TryGetPrimitiveOperatorType(Op, lTy, rTy, out opTy))
            {
                if (opTy == null)
                {
                    // We *might* be dealing with the case where one of the operands can be
                    // converted to the other operand's type via a literal conversion, e.g.,
                    //
                    //     ulong x = 10;
                    //     ulong y = x & 0x2;
                    //
                    // So we should test if one operand is convertible to the other before panicking.
                    if (Scope.HasImplicitConversion(lExpr, rExpr.Type))
                    {
                        opTy = rExpr.Type;
                    }
                    else if (Scope.HasImplicitConversion(rExpr, lExpr.Type))
                    {
                        opTy = lExpr.Type;
                    }
                    else
                    {
                        globalScope.Log.LogError(new LogEntry(
                            "operator application",
                            NodeHelpers.HighlightEven(
                                "operator '", Op.Name, "' cannot be applied to operands of type '",
                                globalScope.TypeNamer.Convert(lTy), "' and '",
                                globalScope.TypeNamer.Convert(rTy), "'."),
                            LeftLocation.Concat(RightLocation)));
                        return new UnknownExpression(lTy);
                    }
                }

                return DirectBinaryExpression.Instance.Create(
                    Scope.ConvertImplicit(lExpr, opTy, LeftLocation),
                    Op,
                    Scope.ConvertImplicit(rExpr, opTy, RightLocation));
            }

            // Enum operators
            IType underlyingTy;
            if (BinaryOperatorResolution.TryGetEnumOperatorType(
                Op, lTy, rTy, out underlyingTy, out opTy))
            {
                var lConv = TryConvertEnumOperand(lExpr, underlyingTy, Scope);

                if (lConv != null)
                {
                    var rConv = TryConvertEnumOperand(rExpr, underlyingTy, Scope);

                    if (rConv != null)
                    {
                        var binOp = DirectBinaryExpression.Instance.Create(
                            lConv, Op, rConv);

                        if (binOp.Type.Equals(opTy))
                            return binOp;
                        else
                            return new StaticCastExpression(binOp, opTy);
                    }
                }
            }

            // User-defined operators.
            // TODO: maybe also consider supporting non-static operators?
            var candidates =
                Scope.Function.GetOperators(lTy, Op)
                    .Union(Scope.Function.GetOperators(rTy, Op))
                    .Where(m => m.IsStatic && m.Parameters.Count() == 2)
                    .Select(m => new GetMethodExpression(m, null))
                    .ToArray();

            var args = new Tuple<IExpression, SourceLocation>[]
            {
                Tuple.Create(lExpr, LeftLocation),
                Tuple.Create(rExpr, RightLocation)
            };
            var argTypes = OverloadResolution.GetArgumentTypes(args);
            var result = OverloadResolution.CreateUncheckedInvocation(
                candidates, args, Scope);
            if (result != null)
            {
                // We found a user-defined operator to apply.
                return result;
            }

            // We didn't find an applicable user-defined operator. 
            // Try reference equality.
            if (BinaryOperatorResolution.IsReferenceEquality(Op, lTy, rTy))
            {
                opTy = Scope.HasImplicitConversion(lTy, rTy)
                    ? rTy : lTy;

                return DirectBinaryExpression.Instance.Create(
                    Scope.ConvertImplicit(lExpr, opTy, LeftLocation),
                    Op,
                    Scope.ConvertImplicit(rExpr, opTy, RightLocation));
            }

            // We couldn't find a single binary operator to apply.
            var errLoc = LeftLocation.Concat(RightLocation);
            if (candidates.Length > 0)
            {
                // If we have discovered candidate user-defined operators
                // in the meantime, then this would be a good time to print them.
                return OverloadResolution.LogFailedOverload(
                    "operator", candidates, args, Scope.Global, 
                    errLoc, argTypes);
            }
            else
            {
                // Print a special message, and return an unknown-expression
                // with as type the lhs's type.
                Scope.Global.Log.LogError(new LogEntry(
                    "operator resolution", 
                    NodeHelpers.HighlightEven(
                        "operator '", Op.Name, 
                        "' could not be applied to operands of types '",
                        Scope.Global.TypeNamer.Convert(lTy),
                        "' and '", Scope.Global.TypeNamer.Convert(rTy),
                        "'."),
                    errLoc));
                return new UnknownExpression(lTy);
            }
        }

        /// <summary>
        /// Creates a converter that analyzes binary operator nodes.
        /// </summary>
        public static Func<LNode, LocalScope, NodeConverter, IExpression> CreateBinaryOpConverter(Operator Op)
        {
            return (node, scope, conv) =>
            {
                if (!NodeHelpers.CheckArity(node, 2, scope.Log))
                    return ErrorTypeExpression;

                return CreateBinary(
                    Op,
                    conv.ConvertValue(node.Args[0], scope),
                    conv.ConvertValue(node.Args[1], scope),
                    scope.Function,
                    NodeHelpers.ToSourceLocation(node.Args[0].Range),
                    NodeHelpers.ToSourceLocation(node.Args[1].Range));
            };
        }

        /// <summary>
        /// Converts the given logical-and expression.
        /// </summary>
        public static IExpression ConvertLogicalAnd(
            LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckArity(Node, 2, Scope.Log))
                return ErrorTypeExpression;

            var lhs = Converter.ConvertExpression(Node.Args[0], Scope, PrimitiveTypes.Boolean);
            var rhs = Converter.ConvertExpression(Node.Args[1], Scope);

            var rhsTy = rhs.Type;
            if (PrimitiveTypes.Boolean.Equals(rhsTy))
            {
                return new LazyAndExpression(lhs, rhs);
            }
            else
            {
                return new SelectExpression(
                    lhs, 
                    rhs, 
                    Scope.Function.ConvertImplicit(
                        new BooleanExpression(false),
                        rhsTy, NodeHelpers.ToSourceLocation(Node.Range)));
            }
        }

        /// <summary>
        /// Converts the given logical-or expression.
        /// </summary>
        public static IExpression ConvertLogicalOr(
            LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckArity(Node, 2, Scope.Log))
                return ErrorTypeExpression;

            var lhs = Converter.ConvertExpression(Node.Args[0], Scope, PrimitiveTypes.Boolean);
            var rhs = Converter.ConvertExpression(Node.Args[1], Scope);

            var rhsTy = rhs.Type;
            if (PrimitiveTypes.Boolean.Equals(rhsTy))
            {
                return new LazyOrExpression(lhs, rhs);
            }
            else
            {
                return new SelectExpression(
                    lhs, 
                    Scope.Function.ConvertImplicit(
                        new BooleanExpression(true),
                        rhsTy, NodeHelpers.ToSourceLocation(Node.Range)),
                    rhs);
            }
        }

        /// <summary>
        /// Determines if the given variable is a local variable.
        /// Loading a local variable has no side-effects.
        /// </summary>
        public static bool IsLocalVariable(IVariable Variable)
        {
            return Variable is LocalVariableBase
                || Variable is ArgumentVariable
                || Variable is ThisVariable;
        }

        /// <summary>
        /// Determines if the given value is a local variable.
        /// Loading a local variable has no side-effects.
        /// </summary>
        public static bool IsLocalVariable(IValue Value)
        {
            if (Value is SourceValue)
                return IsLocalVariable(((SourceValue)Value).Value);
            else if (Value is VariableValue)
                return IsLocalVariable(((VariableValue)Value).Variable);
            else
                return false;
        }

        public static IExpression CreateUncheckedAssignment(
            IValue Variable, IExpression Value, 
            ILocalScope Scope, SourceLocation Location)
        {
            if (IsLocalVariable(Variable))
            {
                return new InitializedExpression(
                    Variable.CreateSetStatementOrError(Value, Scope, Location),
                    Variable.CreateGetExpressionOrError(Scope, Location));
            }
            else
            {
                return new AssignmentExpression(
                    valExpr => Variable.CreateSetStatementOrError(valExpr, Scope, Location), 
                    Value);
            }
        }

        public static IExpression CreateCheckedAssignment(
            IValue Variable, IExpression Value,
            ILocalScope Scope, SourceLocation Location)
        {
            return CreateUncheckedAssignment(
                Variable, Scope.Function.ConvertImplicit(
                    Value, Variable.Type, Location),
                Scope, Location);
        }

        /// <summary>
        /// Converts an assignment node (type @=).
        /// </summary>
        public static IExpression ConvertAssignment(LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckArity(Node, 2, Scope.Log))
                return ErrorTypeExpression;

            var lhs = Converter.ConvertValue(Node.Args[0], Scope);
            var rhs = Converter.ConvertExpression(Node.Args[1], Scope);

            return CreateCheckedAssignment(
                lhs, rhs, Scope,
                NodeHelpers.ToSourceLocation(Node.Args[0].Range).Concat(
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
                    return ErrorTypeExpression;

                var lhs = conv.ConvertValue(node.Args[0], scope);
                var rhs = conv.ConvertValue(node.Args[1], scope);

                var leftLoc = NodeHelpers.ToSourceLocation(node.Args[0].Range);
                var rightLoc = NodeHelpers.ToSourceLocation(node.Args[1].Range);

                var result = CreateBinary(
                    Op, lhs, rhs, scope.Function, leftLoc, rightLoc);

                return CreateUncheckedAssignment(
                    lhs, scope.Function.ConvertImplicit(
                        result, lhs.Type, rightLoc),
                    scope, leftLoc.Concat(rightLoc));
            };
        }

        /// <summary>
        /// Converts a variable declaration node, (type #var)
        /// and returns an (initialization-statement, variable-list)
        /// pair.
        /// </summary>
        public static Tuple<IStatement, IReadOnlyList<IVariable>> ConvertVariableDeclaration(LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckMinArity(Node, 2, Scope.Log))
                return new Tuple<IStatement, IReadOnlyList<IVariable>>(
                    EmptyStatement.Instance, new IVariable[] { });

            var varTyNode = Node.Args[0];
            bool isVar = varTyNode.IsIdNamed(CodeSymbols.Missing);
            IType varTy = null;

            if (!isVar)
            {
                varTy = Converter.ConvertType(varTyNode, Scope);
                if (varTy == null)
                {
                    Scope.Log.LogError(
                        new LogEntry(
                            "type resolution",
                            NodeHelpers.HighlightEven("could not resolve variable type '", Node.ToString(), "'."),
                            NodeHelpers.ToSourceLocation(varTyNode.Range)));
                    return new Tuple<IStatement, IReadOnlyList<IVariable>>(
                        EmptyStatement.Instance, new IVariable[] { });
                }
            }

            var stmts = new List<IStatement>();
            var locals = new List<IVariable>();
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
                            "syntax error",
                            "an implicitly typed local variable declarator " +
                            "must include an initializer.",
                            srcLoc));
                    continue;
                }

                Symbol localName = nameNode.Name;
                var varMember = new DescribedVariableMember(
                    localName.Name, isVar ? val.Type : varTy);
                varMember.AddAttribute(new SourceLocationAttribute(srcLoc));
                var local = Scope.DeclareLocal(localName, varMember);
                if (val != null)
                {
                    stmts.Add(local.CreateSetStatement(
                        Scope.Function.ConvertImplicit(
                            val, varMember.VariableType,
                            NodeHelpers.ToSourceLocation(decompNodes.Item2.Range))));
                }
                locals.Add(local);
            }
            return new Tuple<IStatement, IReadOnlyList<IVariable>>(
                new BlockStatement(stmts), locals);

        }

        /// <summary>
        /// Converts a variable declaration node (type #var).
        /// </summary>
        public static IExpression ConvertVariableDeclarationExpression(LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            var declPair = ConvertVariableDeclaration(Node, Scope, Converter);
            return new InitializedExpression(
                declPair.Item1, declPair.Item2.Last().CreateGetExpression());
        }

        /// <summary>
        /// Converts a 'this'-expression node (type #this).
        /// </summary>
        public static IValue ConvertThisExpression(LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            var thisVar = GetThisVariable(Scope);
            if (thisVar == null)
            {
                return new ErrorValue(new LogEntry(
                    "syntax error",
                    NodeHelpers.HighlightEven(
                        "keyword '", "this",
                        "' is not valid in a static property, static method, or static field initializer."),
                    NodeHelpers.ToSourceLocation(Node.Range)));
            }

            if (Node.IsId)
            {
                // Regular 'this' expression.
                return new VariableValue(thisVar);
            }
            else
            {
                // Constructor call.
                var thisExpr = thisVar.CreateGetExpression();
                var candidates = thisExpr.Type.GetConstructors()
                    .Where(item => !item.IsStatic)
                    .Select(item => new GetMethodExpression(item, thisExpr))
                    .ToArray();

                var args = OverloadResolution.ConvertArguments(Node.Args, Scope, Converter);

                return new ExpressionValue(
                    OverloadResolution.CreateCheckedInvocation(
                        "constructor", candidates, args, Scope.Function,
                        NodeHelpers.ToSourceLocation(Node.Range)));
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
                    "syntax error",
                    NodeHelpers.HighlightEven(
                        "keyword '", "base",
                        "' is not valid in a static property, static method, or static field initializer."),
                    NodeHelpers.ToSourceLocation(Node.Range)));
                return ErrorTypeExpression;
            }

            var baseType = Scope.Function.CurrentType.GetParent();
            if (baseType == null)
            {
                // This can only happen when we're compiling code for
                // a platform that doesn't have a root type.
                Scope.Log.LogError(new LogEntry(
                    "syntax error",
                    NodeHelpers.HighlightEven(
                        "keyword '", "base",
                        "' is only valid for types that have a base type."),
                    NodeHelpers.ToSourceLocation(Node.Range)));
                return ErrorTypeExpression;
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
                    "constructor", candidates, args, Scope.Function,
                    NodeHelpers.ToSourceLocation(Node.Range));
            }
        }

        /// <summary>
        /// Converts an if-statement node (type #if),
        /// and wraps it in a void expression.
        /// </summary>
        public static IExpression ConvertIfExpression(LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckMinArity(Node, 2, Scope.Log)
                || !NodeHelpers.CheckMaxArity(Node, 3, Scope.Log))
                return ErrorTypeExpression;

            var cond = Converter.ConvertExpression(Node.Args[0], Scope, PrimitiveTypes.Boolean);
            var ifExpr = Converter.ConvertScopedStatement(Node.Args[1], Scope);
            var elseExpr = Node.ArgCount == 3
                    ? Converter.ConvertScopedStatement(Node.Args[2], Scope)
                    : EmptyStatement.Instance;

            return ToExpression(new IfElseStatement(cond, ifExpr, elseExpr));
        }

        /// <summary>
        /// Converts a selection-expression. (a ternary
        /// conditional operator application)
        /// </summary>
        public static IExpression ConvertSelectExpression(LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckArity(Node, 3, Scope.Log))
                return ErrorTypeExpression;

            var cond = Converter.ConvertExpression(Node.Args[0], Scope, PrimitiveTypes.Boolean);
            var ifExpr = Converter.ConvertScopedExpression(Node.Args[1], Scope);
            var elseExpr = Converter.ConvertScopedExpression(Node.Args[2], Scope);

            var funScope = Scope.Function;

            var ifType = ifExpr.Type;
            var elseType = elseExpr.Type;

            if (funScope.HasImplicitConversion(ifType, elseType))
            {
                ifExpr = funScope.ConvertImplicit(
                    ifExpr, elseType, NodeHelpers.ToSourceLocation(Node.Args[1].Range));
            }
            else
            {
                elseExpr = funScope.ConvertImplicit(
                    elseExpr, ifType, NodeHelpers.ToSourceLocation(Node.Args[2].Range));
            }

            return new SelectExpression(cond, ifExpr, elseExpr);
        }

        /// <summary>
        /// Converts a break-statement (type #break), and
        /// wraps it in an expression.
        /// </summary>
        public static IExpression ConvertBreakExpression(
            LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (NodeHelpers.CheckCall(Node, Scope.Log))
                NodeHelpers.CheckArity(Node, 0, Scope.Log);

            var tag = Scope.FlowTag;
            if (tag == null)
            {
                Scope.Log.LogError(new LogEntry(
                    "syntax error",
                    NodeHelpers.HighlightEven(
                        "no enclosing loop out of which to ",
                        "break", "."),
                    NodeHelpers.ToSourceLocation(Node.Range)));
                return ErrorTypeExpression;
            }
            else
            {
                return ToExpression(new BreakStatement(Scope.FlowTag));
            }
        }

        /// <summary>
        /// Converts a continue-statement (type #continue), and
        /// wraps it in an expression.
        /// </summary>
        public static IExpression ConvertContinueExpression(
            LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (NodeHelpers.CheckCall(Node, Scope.Log))
                NodeHelpers.CheckArity(Node, 0, Scope.Log);

            var tag = Scope.FlowTag;
            if (tag == null)
            {
                Scope.Log.LogError(new LogEntry(
                    "syntax error",
                    NodeHelpers.HighlightEven(
                        "no enclosing loop out of which to ",
                        "continue", "."),
                    NodeHelpers.ToSourceLocation(Node.Range)));
                return ErrorTypeExpression;
            }
            else
            {
                return ToExpression(new ContinueStatement(Scope.FlowTag));
            }
        }

        /// <summary>
        /// Converts a while-statement node (type #while),
        /// and wraps it in a void expression.
        /// </summary>
        public static IExpression ConvertWhileExpression(
            LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckArity(Node, 2, Scope.Log))
                return ErrorTypeExpression;

            var tag = new UniqueTag("while");

            var cond = Converter.ConvertExpression(Node.Args[0], Scope, PrimitiveTypes.Boolean);
            var body = Converter.ConvertScopedStatement(Node.Args[1], new FlowScope(Scope, tag));

            return ToExpression(new WhileStatement(tag, cond, body));
        }

        /// <summary>
        /// Converts a do-while-statement node (type #doWhile),
        /// and wraps it in a void expression.
        /// </summary>
        public static IExpression ConvertDoWhileExpression(
            LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckArity(Node, 2, Scope.Log))
                return ErrorTypeExpression;

            var tag = new UniqueTag("do-while");

            var body = Converter.ConvertScopedStatement(Node.Args[0], new FlowScope(Scope, tag));
            var cond = Converter.ConvertExpression(Node.Args[1], Scope, PrimitiveTypes.Boolean);

            return ToExpression(new DoWhileStatement(tag, body, cond));
        }

        /// <summary>
        /// Converts a for-statement node (type #for),
        /// and wraps it in a void expression.
        /// </summary>
        public static IExpression ConvertForExpression(
            LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckArity(Node, 4, Scope.Log))
                return ErrorTypeExpression;

            var childScope = new LocalScope(Scope);

            var tag = new UniqueTag("for");

            var init = Converter.ConvertStatementBlock(Node.Args[0].Args, childScope);
            var cond = Converter.ConvertExpression(Node.Args[1], childScope, PrimitiveTypes.Boolean);
            var delta = Converter.ConvertStatementBlock(Node.Args[2].Args, childScope);
            var body = Converter.ConvertScopedStatement(Node.Args[3], new FlowScope(childScope, tag));

            return ToExpression(new ForStatement(tag, init, cond, delta, body, childScope.Release()));
        }

        /// <summary>
        /// Converts a default-value node (type #default).
        /// </summary>
        public static IExpression ConvertDefaultExpression(
            LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckArity(Node, 1, Scope.Log))
                return ErrorTypeExpression;

            var ty = Converter.ConvertCheckedType(Node.Args[0], Scope);

            if (ty == null)
            {
                return ErrorTypeExpression;
            }
            else if (ty.Equals(PrimitiveTypes.Void))
            {
                Scope.Log.LogError(new LogEntry(
                    "syntax error",
                    NodeHelpers.HighlightEven("type '", "void", "' cannot be used in this context."),
                    NodeHelpers.ToSourceLocation(Node.Args[0].Range)));
                return ErrorTypeExpression;
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
                return ErrorTypeExpression;

            var op = Converter.ConvertExpression(Node.Args[0], Scope);
            var ty = Converter.ConvertType(Node.Args[1], Scope);

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

            if (Scope.Function.HasReferenceConversion(op.Type, ty))
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
                    if (Scope.Function.Global.UseWarning(EcsWarnings.AlwaysNullWarning))
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
                else if (Scope.Function.Global.UseWarning(EcsWarnings.RedundantAsWarning))
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
                return ErrorTypeExpression;

            var op = Converter.ConvertExpression(Node.Args[0], Scope);
            var ty = Converter.ConvertType(Node.Args[1], Scope);
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
                bool resultVal = evalResult.GetValue<bool>();
                string lit = resultVal ? "true" : "false";
                var warn = resultVal ? EcsWarnings.AlwaysTrueWarning : EcsWarnings.AlwaysFalseWarning;
                if (Scope.Function.Global.UseWarning(warn))
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
                if (Scope.Function.Global.UseWarning(warn))
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
                return ErrorTypeExpression;

            var op = Converter.ConvertExpression(Node.Args[0], Scope);
            var ty = Converter.ConvertType(Node.Args[1], Scope);

            return Scope.Function.ConvertExplicit(
                op, ty, NodeHelpers.ToSourceLocation(Node.Range));
        }

        /// <summary>
        /// Converts a using-cast expression node (type #usingCast).
        /// </summary>
        public static IExpression ConvertUsingCastExpression(
            LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            // A using-cast has the same meaning as an implicit cast.
            // Syntax:
            //
            //      x using T
            //

            if (!NodeHelpers.CheckArity(Node, 2, Scope.Log))
                return ErrorTypeExpression;

            // Don't log a warning here. Instead, we'll log a warning
            // when `x using T` is used in a context where `using` cast
            // semantics differ from `(T)x` semantics.
            // This is the case when calling a method on `x using T`
            // where `x` is a struct and `T` is not.

            var op = Converter.ConvertExpression(Node.Args[0], Scope);
            var ty = Converter.ConvertType(Node.Args[1], Scope);

            var implConv = Scope.Function.GetImplicitConversion(
                op, ty, NodeHelpers.ToSourceLocation(Node.Range));

            if (implConv.IsBoxing)
                // Create a special using-box expression node here,
                // which we can recognize when analyzing
                // method groups.
                return new UsingBoxExpression(op, ty);
            else
                return implConv.Convert(op, ty);
        }

        /// <summary>
        /// Converts a lock-statement, and wraps it in an expression. (type #lock).
        /// </summary>
        /// <returns>The lock-statement, as an expression.</returns>
        public static IExpression ConvertLockExpression(
            LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckArity(Node, 2, Scope.Log))
                return ErrorTypeExpression;

            var lockee = Converter.ConvertExpression(Node.Args[0], Scope);
            var lockeeTy = lockee.Type;
            var lockBody = ConvertScopedStatement(Converter, Node.Args[1], Scope);

            // Resolve System.Threading.Monitor
            var monitor = Scope.Function.Global.Binder.BindType(
                new QualifiedName("Monitor").Qualify(
                    new QualifiedName("Threading").Qualify(
                        "System")));

            if (monitor == null)
            {
                Scope.Log.LogError(new LogEntry(
                    "binding error",
                    NodeHelpers.HighlightEven(
                        "cannot resolve type '", "System.Threading.Monitor", 
                        "', without which '", "lock", "' statements cannot be " +
                        "compiled successfully."),
                    NodeHelpers.ToSourceLocation(Node.Range)));
                return ToExpression(new BlockStatement(
                    new IStatement[] { ToStatement(lockee), lockBody }));
            }

            var rootTy = Scope.Function.Global.Environment.RootType;
            if (rootTy == null)
            {
                Scope.Log.LogError(new LogEntry(
                    "binding error",
                    NodeHelpers.HighlightEven(
                        "the environment does not have a root type, without which '", 
                        "lock", "' statements cannot be compiled successfully."),
                    NodeHelpers.ToSourceLocation(Node.Range)));
                return ToExpression(new BlockStatement(
                    new IStatement[] { ToStatement(lockee), lockBody }));
            }

            // Find the Enter(object, out bool) method
            var enterMethod = monitor.GetMethod(
                new SimpleName("Enter"), true, PrimitiveTypes.Void, 
                new IType[] { rootTy, PrimitiveTypes.Boolean.MakePointerType(PointerKind.ReferencePointer) });

            if (enterMethod == null)
            {
                Scope.Log.LogError(new LogEntry(
                    "binding error",
                    NodeHelpers.HighlightEven(
                        "cannot resolve method '", "System.Threading.Monitor.Enter(object, out bool)", 
                        "', without which '", "lock", "' statements cannot be " +
                        "compiled successfully."),
                    NodeHelpers.ToSourceLocation(Node.Range)));
                return ToExpression(new BlockStatement(
                    new IStatement[] { ToStatement(lockee), lockBody }));
            }

            // Find the Exit(object) method
            var exitMethod = monitor.GetMethod(
                new SimpleName("Exit"), true, PrimitiveTypes.Void, 
                new IType[] { rootTy });

            if (exitMethod == null)
            {
                Scope.Log.LogError(new LogEntry(
                    "binding error",
                    NodeHelpers.HighlightEven(
                        "cannot resolve method '", "System.Threading.Monitor.Exit(object)", 
                        "', without which '", "lock", "' statements cannot be " +
                        "compiled successfully."),
                    NodeHelpers.ToSourceLocation(Node.Range)));
                return ToExpression(new BlockStatement(
                    new IStatement[] { ToStatement(lockee), lockBody }));
            }

            if (!EcsConversionRules.IsCSharpReferenceType(lockeeTy))
            {
                Scope.Log.LogError(new LogEntry(
                    "type error",
                    NodeHelpers.HighlightEven(
                        "'", "lock", "' statements can only be applied to reference types."),
                    NodeHelpers.ToSourceLocation(Node.Args[0].Range)));
                return ToExpression(new BlockStatement(
                    new IStatement[] { ToStatement(lockee), lockBody }));
            }

            // Now, build an IR tree that looks like this:
            //
            //
            // var lockeeVar = lockee;
            // bool hasEntered = false;
            // try
            // {
            //     System.Threading.Monitor.Enter((object)lockeeVar, out hasEntered);
            //     lockBody;
            // }
            // finally
            // {
            //     if (hasEntered) 
            //         System.Threading.Monitor.Exit((object)lockeeVar);
            // }

            var stmts = new List<IStatement>();
            var lockeeVar = new RegisterVariable("lockee", lockeeTy);
            stmts.Add(lockeeVar.CreateSetStatement(lockee));
            var hasEnteredFlag = new LocalVariable("hasEntered", PrimitiveTypes.Boolean);
            stmts.Add(hasEnteredFlag.CreateSetStatement(new BooleanExpression(false)));
            var lockeeObj = new ReinterpretCastExpression(
                lockeeVar.CreateGetExpression(), rootTy);
            stmts.Add(new TryStatement(
                new BlockStatement(new IStatement[]
                    {
                        new ExpressionStatement(new InvocationExpression(
                            enterMethod, null,
                            new IExpression[]
                            { 
                                lockeeObj,
                                hasEnteredFlag.CreateAddressOfExpression() 
                            })),
                        lockBody
                    }),
                new IfElseStatement(
                    hasEnteredFlag.CreateGetExpression(), 
                    new ExpressionStatement(new InvocationExpression(
                        exitMethod, null,
                        new IExpression[] { lockeeObj })))));

            return ToExpression(new BlockStatement(stmts));
        }

        /// <summary>
        /// Converts a throw-expression (type #throw).
        /// </summary>
        public static IExpression ConvertThrowExpression(LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            NodeHelpers.CheckArity(Node, 1, Scope.Log);

            return ToExpression(new ThrowStatement(Converter.ConvertExpression(Node.Args[0], Scope)));
        }

        /// <summary>
        /// Converts a try-statement node (type #try),
        /// and wraps it in a void expression.
        /// </summary>
        public static IExpression ConvertTryExpression(
            LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckMinArity(Node, 1, Scope.Log))
                return ErrorTypeExpression;

            var tryClauses = new List<IStatement>();
            var catchClauses = new List<CatchClause>();
            var finallyClauses = new List<IStatement>();

            foreach (var clause in Node.Args)
            {
                if (clause.Calls(CodeSymbols.Catch))
                {
                    // 'catch' clause
                    if (!NodeHelpers.CheckArity(clause, 3, Scope.Log))
                        continue;

                    if (!clause.Args[1].IsIdNamed(GSymbol.Empty))
                    {
                        Scope.Log.LogError(new LogEntry(
                            "unsupported feature",
                            NodeHelpers.HighlightEven(
                                "sorry, '", "when", "' has not been implemented yet."),
                            NodeHelpers.ToSourceLocation(clause.Args[1].Range)));
                    }

                    CatchClause resultClause;
                    LocalScope clauseScope;

                    if (clause.Args[0].Calls(CodeSymbols.Var))
                    {
                        var varCall = clause.Args[0];

                        if (!NodeHelpers.CheckArity(varCall, 2, Scope.Log))
                            continue;

                        var exceptionType = Converter.ConvertCheckedTypeOrError(
                            varCall.Args[0], Scope);

                        var exceptionVarName = varCall.Args[1].Name;
                        var exceptionVarDesc = new DescribedVariableMember(
                            exceptionVarName.Name, exceptionType);

                        resultClause = new CatchClause(exceptionVarDesc);
                        clauseScope = new LocalScope(Scope);
                        clauseScope.DeclareLocal(
                            exceptionVarName, exceptionVarDesc, 
                            resultClause.ExceptionVariable);
                    }
                    else
                    {
                        var exceptionType = Converter.ConvertCheckedTypeOrError(
                            clause.Args[0], Scope);
                        resultClause = new CatchClause(new DescribedVariableMember(
                            "exception", exceptionType));
                        clauseScope = Scope;
                    }

                    resultClause.Body = Converter.ConvertScopedStatement(
                        clause.Args[2], clauseScope);

                    // Don't release clauseScope's resources, because 
                    // exception handling variables get special treatment.

                    catchClauses.Add(resultClause);
                }
                else if (clause.Calls(CodeSymbols.Finally))
                {
                    // 'finally' clause
                    if (!NodeHelpers.CheckArity(clause, 1, Scope.Log))
                        continue;

                    finallyClauses.Add(Converter.ConvertScopedStatement(
                        clause.Args[0], Scope));
                }
                else
                {
                    // 'try' clause
                    tryClauses.Add(Converter.ConvertScopedStatement(clause, Scope));
                }
            }

            return ToExpression(new TryStatement(
                new BlockStatement(tryClauses), 
                new BlockStatement(finallyClauses), 
                catchClauses));
        }

        private static LogEntry CreateUsingDisposeError(
            TypeOrExpression Target, LocalScope Scope,
            SourceLocation Location)
        {
            return new LogEntry(
                "method resolution",
                NodeHelpers.HighlightEven(
                    "type '", 
                    Scope.Function.Global.TypeNamer.Convert(Target.ExpressionType),
                    "' used in a '", "using", "' statement must " +
                    "have an unambiguous parameterless '",
                    "Dispose", "' method."),
                Location);
        }

        private static IType FindAnyRefTypeAncestor(
            IType Type, GlobalScope Scope)
        {
            if (Type.GetIsReferenceType())
                return Type;

            var refTyParent = Type.GetAllBaseTypes()
                .FirstOrDefault(x => x.GetIsReferenceType());

            if (refTyParent != null)
                return refTyParent;

            refTyParent = Scope.Environment.RootType; 
            if (refTyParent != null)
                return refTyParent;

            return PrimitiveTypes.Void;
        }

        private static IExpression CreateNullCheck(
            IExpression Expression, GlobalScope Scope)
        {
            var exprTy = Expression.Type;
            if (exprTy.GetIsValueType()
                || PrimitiveTypes.GetIsPrimitive(exprTy))
            {
                // TODO: nullables
                // This cannot possibly be 'null'.
                return new BooleanExpression(false);
            }
            else if (exprTy.GetIsGenericParameter())
            {
                // Generic type parameters have to be boxed,
                // and the result should then be compared to
                // 'null'. 
                Expression = ConversionDescription.ImplicitBoxingConversion
                    .Convert(Expression, FindAnyRefTypeAncestor(exprTy, Scope));
            }
            // Reference types.
            return new EqualityExpression(
                Expression, 
                new ReinterpretCastExpression(
                    NullExpression.Instance, Expression.Type));
        }

        // Creates a 'Dispose' call for a 'using' statement.
        private static IStatement CreateUsingDisposeStatement(
            TypeOrExpression Target, 
            LocalScope Scope, SourceLocation Location)
        {
            var method = ConvertMemberAccess(
                Target, "Dispose", 
                new IType[] { },
                Scope, Location); 

            if (!method.IsExpression)
            {
                Scope.Log.LogError(CreateUsingDisposeError(
                    Target, Scope, Location));
                return EmptyStatement.Instance;
            }

            var inter = IntersectionExpression.GetIntersectedExpressions(
                method.Expression.CreateGetExpressionOrError(Scope, Location))
                .ToArray();

            if (inter.Length == 0)
            {
                Scope.Log.LogError(CreateUsingDisposeError(
                    Target, Scope, Location));
                return EmptyStatement.Instance;
            }

            var resolvedCall = OverloadResolution.CreateUncheckedInvocation(
                inter, new Tuple<IExpression, SourceLocation>[] { },
                Scope.Function);

            if (resolvedCall == null)
            {
                Scope.Log.LogError(CreateUsingDisposeError(
                    Target, Scope, Location));
                return EmptyStatement.Instance;
            }

            return new IfElseStatement(
                new NotExpression(
                    CreateNullCheck(
                        Target.Expression.CreateGetExpressionOrError(Scope, Location), 
                        Scope.Function.Global)), 
                new ExpressionStatement(resolvedCall));
        }

        /// <summary>
        /// Converts a using-statement, and wraps it in an expression. (type #using).
        /// </summary>
        /// <returns>The using-statement, as an expression.</returns>
        public static IExpression ConvertUsingExpression(
            LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            // FIXME: the EC# parser doesn't seem to support multiple
            // variable declarations in 'using' statements.

            if (!NodeHelpers.CheckArity(Node, 2, Scope.Log))
                return ErrorTypeExpression;

            var childScope = new LocalScope(Scope);

            IStatement initStmt;
            IStatement disposeStmt;

            if (Node.Args[0].Calls(CodeSymbols.Var))
            {
                var declPair = ConvertVariableDeclaration(
                    Node.Args[0], childScope, Converter);
                initStmt = declPair.Item1;
                var disposeActions = new List<IStatement>();
                var srcLoc = NodeHelpers.ToSourceLocation(Node.Args[0].Range);
                foreach (var local in declPair.Item2)
                {
                    disposeActions.Add(CreateUsingDisposeStatement(
                        new TypeOrExpression(new VariableValue(local)),
                        Scope, srcLoc));
                }
                disposeStmt = new BlockStatement(disposeActions);
            }
            else
            {
                var usedExpr = Converter.ConvertExpression(
                    Node.Args[0], childScope);
                var local = new LocalVariable("usingTemp", usedExpr.Type);
                initStmt = local.CreateSetStatement(usedExpr);
                disposeStmt = CreateUsingDisposeStatement(
                    new TypeOrExpression(new VariableValue(local)),
                    Scope, NodeHelpers.ToSourceLocation(Node.Args[0].Range));
            }

            var usingBody = Converter.ConvertScopedStatement(Node.Args[1], childScope);

            // Now, build an IR tree that is equivalent to like this:
            //
            //
            // initStmt;
            // try
            // {
            //     usingBody;
            // }
            // finally
            // {
            //     disposeStmt;
            // }

            return ToExpression(new BlockStatement(
                new IStatement[]
                {
                    initStmt,
                    new TryStatement(
                        usingBody, 
                        disposeStmt)
                }));
        }

        /// <summary>
        /// Converts a builtin static-if-expression node (type #builtin_static_if).
        /// </summary>
        public static IExpression ConvertBuiltinStaticIfExpression(LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckMinArity(Node, 2, Scope.Log)
                || !NodeHelpers.CheckMaxArity(Node, 3, Scope.Log))
                return ErrorTypeExpression;

            var cond = Converter.ConvertExpression(Node.Args[0], Scope, PrimitiveTypes.Boolean);
            var result = cond.EvaluateOrNull();
            if (result == null)
            {
                Scope.Log.LogError(new LogEntry(
                    "could not evaluate",
                    NodeHelpers.HighlightEven(
                        "the condition of the '", EcscMacros.EcscSymbols.BuiltinStaticIf.Name, 
                        "' node could not be evaluated."),
                    NodeHelpers.ToSourceLocation(Node.Args[0].Range)));
                return ErrorTypeExpression;
            }
            else if (result.EvaluatesTo<bool>(true))
            {
                return Converter.ConvertScopedExpression(Node.Args[1], Scope);
            }
            else if (result.EvaluatesTo<bool>(false))
            {
                if (Node.ArgCount == 3)
                    return Converter.ConvertScopedExpression(Node.Args[2], Scope);
                else
                    return VoidExpression.Instance;
            }
            else
            {
                // TODO: maybe remove this?
                // This should never happen, because we have already introduced 
                // an implicit cast.
                Scope.Log.LogError(new LogEntry(
                    "type error",
                    NodeHelpers.HighlightEven(
                        "the condition of the '", EcscMacros.EcscSymbols.BuiltinStaticIf.Name, 
                        "' had type '", Scope.Function.Global.TypeNamer.Convert(result.Type),
                        "', but '", "bool", "' was expected."),
                    NodeHelpers.ToSourceLocation(Node.Args[0].Range)));
                return ErrorTypeExpression;
            }
        }

        /// <summary>
        /// Converts a builtin static-is-array node (type #builtin_static_is_array).
        /// </summary>
        public static IExpression ConvertBuiltinStaticIsArrayExpression(LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckMinArity(Node, 1, Scope.Log)
                || !NodeHelpers.CheckMaxArity(Node, 2, Scope.Log))
                return ErrorTypeExpression;

            var ty = Converter.ConvertCheckedTypeOrError(Node.Args[0], Scope);
            if (ty.GetIsArray())
            {
                if (Node.ArgCount == 2)
                {
                    object rank = Node.Args[1].Value;
                    if (rank is int)
                    {
                        return new BooleanExpression(ty.AsArrayType().ArrayRank == (int)rank);
                    }
                    else
                    {
                        Scope.Log.LogError(new LogEntry(
                            "syntax error",
                            NodeHelpers.HighlightEven(
                                "the (optional) second argument of a '", 
                                EcscMacros.EcscSymbols.BuiltinStaticIsArray.Name, 
                                "' node was not an integer literal."),
                            NodeHelpers.ToSourceLocation(Node.Args[1].Range)));
                        return new BooleanExpression(false);
                    }
                }
                else
                {
                    return new BooleanExpression(true);
                }
            }
            else
            {
                return new BooleanExpression(false);
            }
        }

        /// <summary>
        /// Converts a builtin decltype node (type #builtin_decltype).
        /// </summary>
        public static IType ConvertBuiltinDecltype(LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckArity(Node, 1, Scope.Log))
                return null;

            var expr = Converter.ConvertExpression(Node.Args[0], Scope);
            return expr.Type;
        }

        /// <summary>
        /// Parses the given node as a warning name.
        /// </summary>
        /// <param name="NameNode">The node to parse.</param>
        /// <param name="Scope">The current scope, which may be used for error logging.</param>
        /// <returns>The warning name, as a string. Null if the node was not a warning name.</returns>
        private static string ConvertWarningName(LNode NameNode, LocalScope Scope)
        {
            if (NameNode.IsLiteral && NameNode.Value != null && NameNode.Value is string)
                return (string)NameNode.Value;

            Scope.Log.LogError(
                new LogEntry(
                    "syntax error",
                    "expected a warning name formatted as a string literal.",
                    NodeHelpers.ToSourceLocation(NameNode.Range)));
            return null;
        }

        /// <summary>
        /// Parses the given nodes as warning names.
        /// </summary>
        /// <param name="NameNodes">The nodes to parse.</param>
        /// <param name="Scope">The current scope, which may be used for error logging.</param>
        /// <returns>An array of all warning names that were successfully parsed.</returns>
        private static string[] ConvertWarningNames(IEnumerable<LNode> NameNodes, LocalScope Scope)
        {
            var warningNames = new List<string>();
            foreach (var warningNameNode in NameNodes)
            {
                var name = ConvertWarningName(warningNameNode, Scope);
                if (name != null)
                    warningNames.Add(name);
            }
            return warningNames.ToArray();
        }

        /// <summary>
        /// Converts a builtin disable-warning node (type #builtin_warning_disable).
        /// </summary>
        public static TypeOrExpression ConvertBuiltinDisableWarning(LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckMinArity(Node, 1, Scope.Log))
                return null;

            var warningNames = ConvertWarningNames(Node.Args.Slice(0, Node.Args.Count - 1), Scope);
            var contents = Node.Args.Last;
            return Converter.ConvertTypeOrExpression(
                contents,
                new LocalScope(
                    new AlteredFunctionScope(Scope, Scope.Function.DisableWarnings(warningNames))));
        }

        /// <summary>
        /// Converts a builtin restore-warning node (type #builtin_warning_restore).
        /// </summary>
        public static TypeOrExpression ConvertBuiltinRestoreWarning(LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckMinArity(Node, 1, Scope.Log))
                return null;

            var warningNames = ConvertWarningNames(Node.Args.Slice(0, Node.Args.Count - 1), Scope);
            var contents = Node.Args.Last;
            return Converter.ConvertTypeOrExpression(
                contents,
                new LocalScope(
                    new AlteredFunctionScope(Scope, Scope.Function.RestoreWarnings(warningNames))));
        }
    }
}
