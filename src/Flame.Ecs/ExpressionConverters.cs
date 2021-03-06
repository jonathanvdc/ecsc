﻿using System;
using System.Collections.Generic;
using System.Linq;
using Flame.Build;
using Flame.Compiler;
using Flame.Compiler.Expressions;
using Flame.Compiler.Statements;
using Flame.Compiler.Variables;
using Flame.Compiler.Emit;
using Flame.Ecs.Diagnostics;
using Flame.Ecs.Semantics;
using Loyc;
using Loyc.Syntax;
using Pixie;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using Flame.Collections;
using Flame.Ecs.Values;
using EcscMacros;

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
            if (TypeArguments.Count > 0)
            {
                if (MethodResult != null)
                {
                    if (!CheckGenericConstraints(MethodResult, TypeArguments, Scope, Location))
                        // Just ignore this method for now.
                        return;

                    member = MethodResult.MakeGenericMethod(TypeArguments);
                }
                else
                {
                    LogCannotInstantiate(Member.Name.ToString(), Scope, Location);
                    return;
                }
            }

            var acc = AccessMember(TargetExpression, member, Scope);
            if (acc != null)
                Results.Add(acc);
        }

        /// <summary>
        /// Checks if the given member is a constructor.
        /// </summary>
        /// <param name="Member">The member to check.</param>
        /// <returns><c>true</c> if the given member is a constructor; otherwise, <c>false</c>.</returns>
        private static bool IsConstructorMethod(ITypeMember Member)
        {
            return Member is IMethod && ((IMethod)Member).IsConstructor;
        }

        private static void CreateUnqualifiedNameExpressionAccess(
            Symbol Name, IReadOnlyList<IType> TypeArguments, ILocalScope Scope,
            SourceLocation Location, HashSet<IValue> Results, ref IMethod MethodResult)
        {
            if (TypeArguments.Count == 0)
            {
                // Early-out for local variables.
                var local = Scope.GetVariable(Name);
                if (local != null)
                {
                    Results.Add(new VariableValue(local));
                    return;
                }
            }

            var nameString = Name.Name;
            foreach (var item in Scope.Function.GetUnqualifiedStaticMembers(nameString))
            {
                if (IsConstructorMethod(item))
                {
                    continue;
                }

                CreateMemberAccess(
                    item, TypeArguments, null, Results,
                    Scope.Function.Global, Location, ref MethodResult);
            }

            var declType = Scope.Function.DeclaringType;

            if (declType != null)
            {
                var thisVar = GetThisVariable(Scope);
                if (GetThisVariable(Scope) != null)
                {
                    foreach (var item in Scope.Function.GetInstanceAndExtensionMembers(declType, nameString))
                    {
                        if (IsConstructorMethod(item))
                        {
                            continue;
                        }

                        CreateMemberAccess(
                            item, TypeArguments, new VariableValue(thisVar), Results,
                            Scope.Function.Global, Location, ref MethodResult);
                    }
                }
            }
        }

        private static IValue LookupUnqualifiedNameExpression(
            Symbol Name, IReadOnlyList<IType> TypeArguments, ILocalScope Scope,
            SourceLocation Location)
        {
            IMethod method = null;

            // Create a set of potential results.
            var exprSet = new HashSet<IValue>();
            CreateUnqualifiedNameExpressionAccess(
                Name, TypeArguments, Scope, Location, exprSet, ref method);

            if (exprSet.Count == 0)
            {
                if (method != null)
                {
                    // We want to provide a diagnostic if we
                    // encountered a method that was not given the right
                    // amount of type parameters.
                    return new ErrorValue(CreateGenericArityMismatchEntry(method, TypeArguments.Count, Location));
                }
                else
                {
                    return new ErrorValue(() =>
                    {
                        // If the set of expressions is empty, then we want to return
                        // an error value.
                        // First of all, let's see if we can guess what the user meant.
                        var suggestedName = NameSuggestionHelpers.SuggestName(
                            Name.Name,
                            Scope.Function.GetUnqualifiedStaticMembers()
                                .Concat(Scope.Function.DeclaringType == null
                                    || (Scope.Function.CurrentMethod != null
                                        && Scope.Function.CurrentMethod.IsStatic)
                                    ? Enumerable.Empty<ITypeMember>()
                                    : Scope.Function.GetInstanceMembers(Scope.Function.DeclaringType))
                                .Select(member => member.Name)
                                .OfType<SimpleName>()
                                .Select(member => member.Name)
                                .Concat(
                                    TypeArguments.Count == 0
                                    ? Scope.VariableNames
                                        .Where(symbol => symbol.Pool == Name.Pool)
                                        .Select(symbol => symbol.Name)
                                    : Enumerable.Empty<string>()));

                        return new LogEntry(
                            "name lookup",
                            NodeHelpers.HighlightEven(
                                "name '",
                                Name.Name,
                                "' is not defined here.")
                                .Concat(
                                    suggestedName == null
                                    ? Enumerable.Empty<MarkupNode>()
                                    : NodeHelpers.HighlightEven(
                                        " Did you mean '", suggestedName, "'?")
                                .ToArray()),
                            Location);
                    });
                }
            }
            else
            {
                return IntersectionValue.Create(exprSet);
            }
        }

        private static IEnumerable<IType> LookupUnqualifiedNameTypes(QualifiedName Name, ILocalScope Scope)
        {
            var ty = Scope.Function.Global.Binder.BindType(Name);
            if (ty == null)
                return Enumerable.Empty<IType>();
            else
                return new IType[] { ty };
        }

        private static IEnumerable<IType> LookupNameTypeInstances(
            QualifiedName Qualifier, string Name,
            IReadOnlyList<IType> TypeArguments, ILocalScope Scope,
            SourceLocation Location)
        {
            var genericName = new SimpleName(Name, TypeArguments.Count).Qualify(Qualifier);
            var ty = Scope.Function.Global.Binder.BindType(genericName);
            if (ty == null)
            {
                return Enumerable.Empty<IType>();
            }
            else
            {
                return InstantiateTypes(new IType[] { ty }, TypeArguments, Scope.Function.Global, Location);
            }
        }

        public static TypeOrExpression LookupUnqualifiedName(LNode Name, ILocalScope Scope)
        {
            var qualName = new QualifiedName(Name.Name.Name);
            return new TypeOrExpression(
                LookupUnqualifiedNameExpression(
                    Name.Name, new IType[] { }, Scope,
                    NodeHelpers.ToSourceLocation(Name.Range)),
                LookupUnqualifiedNameTypes(qualName, Scope),
                qualName);
        }

        public static TypeOrExpression LookupUnqualifiedNameInstance(
            Symbol Name, IReadOnlyList<IType> TypeArguments, ILocalScope Scope,
            SourceLocation Location)
        {
            return new TypeOrExpression(
                LookupUnqualifiedNameExpression(Name, TypeArguments, Scope, Location),
                LookupNameTypeInstances(new QualifiedName(), Name.Name, TypeArguments, Scope, Location),
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
        /// Creates a value that can be used as the target object for a
        /// member-access expression.
        /// </summary>
        public static ResultOrError<IExpression, LogEntry> AsTargetValue(
            IValue Value, IType TargetType, ILocalScope Scope,
            SourceLocation Location, bool CreateTemporary)
        {
            if (Value == null)
            {
                return ResultOrError<IExpression, LogEntry>.FromResult(null);
            }

            var valType = Value.Type;
            if (valType.GetIsReferenceType())
            {
                var result = Value.CreateGetExpression(Scope, Location);
                if (!valType.Equals(TargetType) && TargetType.GetIsReferenceType())
                {
                    return result.MapResult<IExpression>(expr =>
                        new ReinterpretCastExpression(expr, TargetType));
                }
                else
                {
                    return result;
                }
            }
            else
            {
                var address = CreateTemporary
                    ? ToValueAddress(Value, Scope, Location)
                    : Value.CreateAddressOfExpression(Scope, Location);

                if (!valType.Equals(TargetType) && !TargetType.GetIsReferenceType())
                {
                    return address.MapResult<IExpression>(expr =>
                        new ReinterpretCastExpression(
                            expr,
                            TargetType.MakePointerType(
                                expr.Type.AsPointerType().PointerKind)));
                }
                else
                {
                    return address;
                }
            }
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
            Scope.Labels.CheckAllMarked(Scope.Global.Log);
            Scope.Labels.CheckAllUsed(Scope.Global.Log);

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
            if (Member.GetIsHidden())
            {
                return null;
            }

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
                        var boxedVal = UsingBoxValue.GetBoxedValue(Target);
                        if (boxedVal == null)
                        {
                            return AsTargetValue(Target, method.DeclaringType, scope, srcLoc, true)
                                .MapResult<IExpression>(targetExpr =>
                            {
                                return new GetMethodExpression(method, targetExpr);
                            });
                        }
                        else
                        {
                            return AsTargetValue(boxedVal, method.DeclaringType, scope, srcLoc, true)
                                .MapResult<IExpression>(targetExpr =>
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
            if (Node.Attrs.Any(item => item.IsIdNamed(CodeSymbols.Yield)))
            {
                // We stumbled across a yield return, which is totally different
                // from a regular return.
                if (!NodeHelpers.CheckArity(Node, 1, Scope.Log))
                    return ErrorTypeExpression;

                var retType = Scope.Function.ReturnType;
                if (!retType.GetIsEnumerableType())
                {
                    Scope.Log.LogError(new LogEntry(
                        "syntax error",
                        NodeHelpers.HighlightEven(
                            "methods cannot use ", "yield return",
                            " unless they have an enumerable return type."),
                        NodeHelpers.ToSourceLocation(Node.Range)));
                }

                var elemType = retType.GetEnumerableElementType();
                if (elemType == null)
                    return ErrorTypeExpression;
                
                return ToExpression(new YieldReturnStatement(
                    Scope.Function.ConvertImplicit(
                        Converter.ConvertExpression(Node.Args[0], Scope),
                        elemType, NodeHelpers.ToSourceLocation(Node.Args[0].Range))));
            }

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
                            "non-void returning function return a value. (expected return type: '",
                            Scope.Function.Global.Renderer.AbbreviateTypeNames(
                                SimpleTypeFinder.Instance.Convert(Scope.ReturnType)).Name(Scope.ReturnType),
                            "')"),
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

        private static LogEntry CreateGenericArityMismatchEntry(
            IGenericMember Declaration, int ArgumentCount,
            SourceLocation Location)
        {
            // Invalid number of type arguments.
            return new LogEntry(
                "generic arity mismatch",
                NodeHelpers.HighlightEven(
                    "'", Declaration.Name.ToString(), "' takes '", Declaration.GenericParameters.Count().ToString(),
                    "' type parameters, but was given '", ArgumentCount.ToString(), "'."),
                Location);
        }

        private static void LogGenericArityMismatch(
            IGenericMember Declaration, int ArgumentCount,
            GlobalScope Scope, SourceLocation Location)
        {
            // Invalid number of type arguments.
            Scope.Log.LogError(CreateGenericArityMismatchEntry(Declaration, ArgumentCount, Location));
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
                if (!tParam.Constraint.Transform(conv).Satisfies(
                    Scope.Environment.GetEquivalentType(tArg)))
                {
                    // Check that this type argument is okay for
                    // the parameter's (transformed) constraints.
                    var abbrevRenderer = Scope.CreateAbbreviatingRenderer(tArg, tParam);

                    Scope.Log.LogError(new LogEntry(
                        "generic constraint",
                        NodeHelpers.HighlightEven(
                            "type '", abbrevRenderer.Name(tArg),
                            "' does not satisfy the generic constraints on type parameter '",
                            abbrevRenderer.Name(tParam), "'."),
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

        private static IValue HandleFailedMemberAccess(
            TypeOrExpression Target, string MemberName,
            LocalScope Scope, SourceLocation TargetLocation,
            SourceLocation MemberLocation)
        {
            if (Target.IsExpression)
            {
                if (ErrorType.Instance.Equals(Target.Expression.Type))
                {
                    // Don't log an error if we tried to access a field on an error expression
                    // because logging an error here will subject the user to a cascade of
                    // unhelpful error messages.
                    return Target.Expression;
                }
                else
                {
                    // The member-access expression tries to access a member on an expression
                    // whose type is not the error type. Return an error value, which will
                    // log an error only if it's used.
                    return new ErrorValue(() =>
                        CreateFailedMemberAccessLogEntry(
                            "value of type",
                            Target.Expression.Type,
                            MemberName,
                            Scope.Function.GetInstanceAndExtensionMembers(Target.Expression.Type),
                            MemberLocation,
                            Scope));
                }
            }

            if (Target.IsType)
            {
                if (Target.Types.Contains(ErrorType.Instance))
                {
                    // Don't log an error if we tried to access a field on an error type
                    // because logging an error here will subject the user to a cascade of
                    // unhelpful error messages.
                    return new ExpressionValue(ErrorTypeExpression);
                }
                else
                {
                    // The member-access expression tries to access a member on a type
                    // which is not the error type. Return an error value, which will
                    // log an error only if it's used.
                    var typeArray = Target.Types.ToArray();
                    return new ErrorValue(() =>
                    {
                        var intersectionType = typeArray[0];
                        foreach (var type in typeArray.Skip(1))
                        {
                            intersectionType = new IntersectionType(intersectionType, type);
                        }
                        return CreateFailedMemberAccessLogEntry(
                            "type",
                            intersectionType,
                            MemberName,
                            typeArray.SelectMany(Scope.Function.GetStaticMembers).Distinct(),
                            MemberLocation,
                            Scope);
                    });
                }
            }
            else
            {
                return new ErrorValue(
                    new LogEntry(
                        "expression resolution",
                        NodeHelpers.HighlightEven(
                            "expression is neither a type nor a value."),
                        TargetLocation));
            }
        }

        private static LogEntry CreateFailedMemberAccessLogEntry(
            string AccessedValueKind, IType AccessedValueType,
            string MemberName, IEnumerable<ITypeMember> AllTypeMembers,
            SourceLocation Location, LocalScope Scope)
        {
            var suggestedName = NameSuggestionHelpers.SuggestName(
                MemberName,
                AllTypeMembers
                    .Select(item => item.Name)
                    .OfType<SimpleName>()
                    .Select(item => item.Name));
            return new LogEntry(
                "member access",
                NodeHelpers.HighlightEven(new string[]
                {
                    AccessedValueKind + " '",
                    Scope.Function.Global.NameAbbreviatedType(AccessedValueType),
                    "' does not expose a member called '",
                    MemberName
                }.Concat(
                    suggestedName == null
                    ? new string[] { "'." }
                    : new string[]
                {
                    "'. Did you mean '",
                    suggestedName,
                    "'?"
                }).ToArray()),
                Location);
        }

        private static TypeOrExpression ConvertMemberAccess(
            TypeOrExpression Target, string MemberName,
            IReadOnlyList<IType> TypeArguments, LocalScope Scope,
            SourceLocation TargetLocation,
            SourceLocation MemberLocation)
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
                foreach (var item in Scope.Function.GetInstanceMembers(targetTy, MemberName))
                {
                    CreateMemberAccess(
                        item, TypeArguments, Target.Expression, exprSet,
                        Scope.Function.Global, MemberLocation, ref method);
                }

                if (exprSet.Count == 0)
                {
                    // The spec states that extension member lookup is a last resort, so we should only
                    // use it if we can't find any applicable instance members.
                    //
                    //     Otherwise, an attempt is made to process E.I as an extension method invocation
                    //     (Extension method invocations). If this fails, E.I is an invalid member
                    //     reference, and a binding-time error occurs.
                    //
                    foreach (var item in Scope.Function.GetExtensionMembers(targetTy, MemberName))
                    {
                        CreateMemberAccess(
                            item, TypeArguments, Target.Expression, exprSet,
                            Scope.Function.Global, MemberLocation, ref method);
                    }
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
                            Scope.Function.Global, MemberLocation, ref method);
                    }
                }
            }

            IValue expr;
            if (exprSet.Count == 0)
            {
                if (method != null)
                {
                    // We want to provide a diagnostic here if we encountered a
                    // method that was not given the right amount of type arguments.
                    LogGenericArityMismatch(method, TypeArguments.Count, Scope.Function.Global, MemberLocation);
                    // Add the error-type expression to the set to keep the node
                    // converter from logging a diagnostic about the fact that
                    // the expression returned here is not a value.
                    expr = new ExpressionValue(ErrorTypeExpression);
                }
                else
                {
                    expr = HandleFailedMemberAccess(Target, MemberName, Scope, TargetLocation, MemberLocation);
                }
            }
            else
            {
                expr = IntersectionValue.Create(exprSet);
            }

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
                    var resolvedTypes = ((INamespace)ty).Types.Where(item =>
                    {
                        var itemName = item.Name as SimpleName;
                        return itemName.Name == MemberName
                            && itemName.TypeParameterCount == TypeArguments.Count;
                    });
                    typeSet.UnionWith(
                        InstantiateTypes(
                            resolvedTypes, TypeArguments,
                            Scope.Function.Global, MemberLocation));
                }
            }
            if (Target.IsNamespace)
            {
                typeSet.UnionWith(
                    LookupNameTypeInstances(
                        Target.Namespace, MemberName,
                        TypeArguments, Scope, MemberLocation));
            }

            return new TypeOrExpression(expr, typeSet, nsName);
        }

        /// <summary>
        /// Converts the given member access node (type @.).
        /// </summary>
        public static TypeOrExpression ConvertMemberAccess(
            LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckArity(Node, 2, Scope.Log))
                return TypeOrExpression.Empty;

            var lhs = Node.Args[0];
            var rhs = Node.Args[1];

            var target = Converter.ConvertTypeOrExpression(lhs, Scope);

            var targetLoc = NodeHelpers.ToSourceLocation(lhs.Range);
            var memberLoc = NodeHelpers.ToSourceLocation(rhs.Range);

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
                    memberLoc));
                return TypeOrExpression.Empty;
            }

            return ConvertMemberAccess(target, ident, tArgs, Scope, targetLoc, memberLoc);
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
                return new TypeOrExpression(elemTy.MakeArrayType(arrayDims));
            }

            // Not an array. How about a pointer?
            if (target.IsIdNamed(CodeSymbols._Pointer))
            {
                NodeHelpers.CheckArity(Node, 2, Scope.Log);

                var elemTy = Converter.ConvertCheckedTypeOrError(args[0], Scope);
                return new TypeOrExpression(elemTy.MakePointerType(PointerKind.TransientPointer));
            }

            // Maybe we encountered a delegate type.
            if (target.IsIdNamed(EcscSymbols.BuiltinDelegateType))
            {
                return new TypeOrExpression(
                    ConvertBuiltinDelegateType(
                        target.WithArgs(args.ToArray<LNode>()),
                        Scope,
                        Converter));
            }

            // Why, it must be a generic instance, then.
            var tArgs = args.Select(item =>
                Converter.ConvertCheckedTypeOrError(item, Scope)).ToArray();

            // The target of a generic instance is either
            // an unqualified expression (i.e. an Id node),
            // or some member-access expression. (type @.)

            if (target.IsId)
            {
                var srcLoc = NodeHelpers.ToSourceLocation(Node.Range);
                return LookupUnqualifiedNameInstance(
                    target.Name, tArgs, Scope, srcLoc);
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
                        NodeHelpers.ToSourceLocation(target.Args[1].Range)));
                    return TypeOrExpression.Empty;
                }

                var ident = target.Args[1].Name.Name;

                return ConvertMemberAccess(
                    targetTyOrExpr, ident, tArgs, Scope,
                    NodeHelpers.ToSourceLocation(target.Args[0].Range),
                    NodeHelpers.ToSourceLocation(target.Args[1].Range));
            }
            else
            {
                var srcLoc = NodeHelpers.ToSourceLocation(Node.Range);
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
            // These C# new-expressions
            //
            //     new T(args...) { values... }
            //     new T[args...] { values... }
            //     new[] { values... }
            //     new { values... }
            //
            // are converted to the following Loyc trees
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
                    if (arrTy.ArrayRank == 1)
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
                        "an object creation expression cannot create an instance of a pointer type."),
                    loc));
                return new UnknownExpression(ctorType);
            }
            else if (ctorType.GetIsStaticType())
            {
                Scope.Log.LogError(new LogEntry(
                    "object creation",
                    NodeHelpers.HighlightEven(
                        "cannot create an instance of static type '",
                        Scope.Function.Global.NameAbbreviatedType(ctorType),
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
                        Scope.Function.Global.NameAbbreviatedType(ctorType),
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
            var curType = Scope.Function.DeclaringType;
            Debug.Assert(curType != null);

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
            foreach (var baseType in Scope.Function.Global.Binder.Environment.GetDefaultBaseTypes(
                anonTy, anonTy.BaseTypes))
            {
                anonTy.AddBaseType(baseType);
            }
            var rootTy = anonTy.GetParent();

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
                            "root type '", Scope.Function.Global.NameAbbreviatedType(rootTy),
                            "' does not have a parameterless constructor."),
                        Location));
                    ctor.Body = new ReturnStatement();
                }
                else
                {
                    // A generic instance of the anonymous type, instantiated with
                    // its own generic parameters.
                    var anonGenericTyInst = anonTy.MakeRecursiveGenericType(anonTy.GetRecursiveGenericParameters());

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
            var anonMethodTyInst = anonTy.MakeRecursiveGenericType(
                curType.GetRecursiveGenericArguments().Concat(genericParams));

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
                if (anonMethodTyInst is GenericTypeBase)
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
                            "cannot resolve initialized member '", fieldName, "'."),
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
                if (!valTy.Equals(ErrorType.Instance))
                {
                    var abbreviatingNamer = fScope.Global.CreateAbbreviatingRenderer(valTy, PrimitiveTypes.String);
                    fScope.Global.Log.LogError(new LogEntry(
                        "missing conversion",
                        NodeHelpers.HighlightEven(
                            "value of type '", abbreviatingNamer.Name(valTy),
                            "' cannot not be converted to type '",
                            abbreviatingNamer.Name(PrimitiveTypes.String),
                            "', because it does not have a parameterless, non-generic '",
                            "ToString", "' method that returns a '",
                            abbreviatingNamer.Name(PrimitiveTypes.String),
                            "' instance."),
                        Location));
                }
                return new UnknownExpression(PrimitiveTypes.String);
            }
            else if (toStringMethods.Length > 1)
            {
                // This shouldn't happen, but we should check for it anyway.
                var abbreviatingNamer = fScope.Global.CreateAbbreviatingRenderer(valTy, PrimitiveTypes.String);
                fScope.Global.Log.LogError(new LogEntry(
                    "missing conversion",
                    NodeHelpers.HighlightEven(
                        "value of type '", abbreviatingNamer.Name(valTy),
                        "' cannot not be converted to type '",
                        abbreviatingNamer.Name(PrimitiveTypes.String),
                        "', there is more than one parameterless, non-generic '",
                        "ToString", "' method that returns a '",
                        abbreviatingNamer.Name(PrimitiveTypes.String),
                        "' instance."),
                    Location));
                return new UnknownExpression(PrimitiveTypes.String);
            }

            var targetVal = AsTargetValue(
                Value, toStringMethods[0].DeclaringType, Scope, Location, true);
            if (targetVal.IsError)
            {
                fScope.Global.Log.LogError(targetVal.Error);
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

        private static IExpression LogNoBinaryOperator(
            IType LeftType,
            Operator Op,
            IType RightType,
            SourceLocation LeftLocation,
            SourceLocation RightLocation,
            GlobalScope Scope)
        {
            var abbreviatingRenderer = Scope.CreateAbbreviatingRenderer(LeftType, RightType);
            Scope.Log.LogError(new LogEntry(
                "operator application",
                NodeHelpers.HighlightEven(
                    "operator '", Op.Name, "' cannot be applied to operands of type '",
                    abbreviatingRenderer.Name(LeftType), "' and '",
                    abbreviatingRenderer.Name(RightType), "'."),
                LeftLocation.Concat(RightLocation)));
            return ErrorTypeExpression;
        }

        /// <summary>
        /// Creates a binary operator application expression
        /// for the given operator and operands. A scope is
        /// used to perform conversions and log error messages,
        /// and two source locations are used to highlight potential
        /// issues.
        /// </summary>
        /// <param name="Op">The operator to apply.</param>
        /// <param name="Left">The left-hand side operand.</param>
        /// <param name="Right">The right-hand side operand.</param>
        /// <param name="Scope">The function scope in which the binary operator application is analyzed.</param>
        /// <param name="LeftLocation">The source location of the left-hand side operand.</param>
        /// <param name="RightLocation">The source location of the right-hand side operand.</param>
        /// <returns>An expression that represents the operator applied to the operands.</returns>
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
                        return LogNoBinaryOperator(lTy, Op, rTy, LeftLocation, RightLocation, globalScope);
                    }
                }

                return CreatePrimitiveBinaryExpression(
                    Op, lExpr, rExpr, opTy, Scope, LeftLocation, RightLocation);
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

            // Pointer arithmetic.
            if (BinaryOperatorResolution.TryGetPointerOperatorType(
                Op, lTy, rTy, out opTy))
            {
                // The C# spec states the following on pointer arithmetic:
                //
                //     In an unsafe context, the + and - operators (Addition operator and
                //     Subtraction operator) can be applied to values of all pointer
                //     types except void*. Thus, for every pointer type T*, the following
                //     operators are implicitly defined:
                //
                //         T* operator +(T* x, int y);
                //         T* operator +(T* x, uint y);
                //         T* operator +(T* x, long y);
                //         T* operator +(T* x, ulong y);
                //
                //         T* operator +(int x, T* y);
                //         T* operator +(uint x, T* y);
                //         T* operator +(long x, T* y);
                //         T* operator +(ulong x, T* y);
                //
                //         T* operator -(T* x, int y);
                //         T* operator -(T* x, uint y);
                //         T* operator -(T* x, long y);
                //         T* operator -(T* x, ulong y);
                //
                //         long operator -(T* x, T* y);

                if (Op.Equals(Operator.Subtract) && opTy.GetIsInteger())
                {
                    return new DivideExpression(
                        new SubtractExpression(
                            new StaticCastExpression(lExpr, opTy),
                            new StaticCastExpression(rExpr, opTy)),
                        new SizeOfExpression(lTy.AsPointerType().ElementType));
                }
                else if (lTy.GetIsPointer())
                {
                    return DirectBinaryExpression.Instance.Create(
                        lExpr, Op, rExpr);
                }
                else
                {
                    // The result of a simple binary IR op is always the LHS. So, to
                    // produce the right return type, we need to swap the LHS and RHS.
                    // Swapping them is legal, because only addition supports integer
                    // values as RHS for pointer arithmetic.

                    var temp = new SSAVariable("tmp", lExpr.Type);
                    return new InitializedExpression(
                        temp.CreateSetStatement(lExpr),
                        new AddExpression(
                            rExpr,
                            temp.CreateGetExpression()));
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
                return LogNoBinaryOperator(lTy, Op, rTy, LeftLocation, RightLocation, globalScope);
            }
        }

        private static IExpression CreatePrimitiveBinaryExpression(
            Operator Op,
            IExpression Left,
            IExpression Right,
            IType OpType,
            FunctionScope Scope,
            SourceLocation LeftLocation,
            SourceLocation RightLocation)
        {
            // Shift operators are special because they have asymmetric operator
            // types in C# but symmetric operator types in Flame IR.
            var convRhs =
                Op.Equals(Operator.LeftShift) || Op.Equals(Operator.RightShift)
                ? Scope.ConvertExplicit(Right, OpType, RightLocation)
                : Scope.ConvertImplicit(Right, OpType, RightLocation);

            return DirectBinaryExpression.Instance.Create(
                Scope.ConvertImplicit(Left, OpType, LeftLocation),
                Op,
                convRhs);
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
        /// Converts a call to the asterisk operator, which can be a multiplication
        /// or a pointer dereference operation.
        /// </summary>
        public static IValue ConvertMultiplyOrDereference(
            LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckMinArity(Node, 1, Scope.Log)
                || !NodeHelpers.CheckMaxArity(Node, 2, Scope.Log))
            {
                return new ExpressionValue(ErrorTypeExpression);
            }

            if (Node.ArgCount == 2)
            {
                return new ExpressionValue(CreateBinary(
                    Operator.Multiply,
                    Converter.ConvertValue(Node.Args[0], Scope),
                    Converter.ConvertValue(Node.Args[1], Scope),
                    Scope.Function,
                    NodeHelpers.ToSourceLocation(Node.Args[0].Range),
                    NodeHelpers.ToSourceLocation(Node.Args[1].Range)));
            }
            else
            {
                var address = Converter.ConvertExpression(Node.Args[0], Scope);
                var addressType = address.Type;
                if (addressType == ErrorType.Instance)
                {
                    return new ExpressionValue(ErrorTypeExpression);
                }

                var addressPtrType = addressType.AsPointerType();
                if (addressPtrType == null
                    || !addressPtrType.PointerKind.Equals(PointerKind.TransientPointer))
                {
                    return new ErrorValue(new LogEntry(
                        "type error",
                        NodeHelpers.HighlightEven(
                            "cannot dereference non-pointer type '",
                            Scope.Function.Global.NameAbbreviatedType(addressType),
                            "'."),
                        NodeHelpers.ToSourceLocation(Node.Args[0].Range)));
                }
                else if (addressPtrType.ElementType == PrimitiveTypes.Void)
                {
                    return new ErrorValue(new LogEntry(
                        "type error",
                        NodeHelpers.HighlightEven(
                            "cannot dereference a pointer to type '",
                            Scope.Function.Global.NameAbbreviatedType(addressPtrType.ElementType),
                            "'."),
                        NodeHelpers.ToSourceLocation(Node.Args[0].Range)));
                }
                else
                {
                    return new VariableValue(new AtAddressVariable(address));
                }
            }
        }

        /// <summary>
        /// Converts a call to the and-bits operator, which can be a bitwise and
        /// or an address-of operation.
        /// </summary>
        public static IExpression ConvertAndOrAddressOf(
            LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckMinArity(Node, 1, Scope.Log)
                || !NodeHelpers.CheckMaxArity(Node, 2, Scope.Log))
            {
                return ErrorTypeExpression;
            }

            if (Node.ArgCount == 2)
            {
                return CreateBinary(
                    Operator.And,
                    Converter.ConvertValue(Node.Args[0], Scope),
                    Converter.ConvertValue(Node.Args[1], Scope),
                    Scope.Function,
                    NodeHelpers.ToSourceLocation(Node.Args[0].Range),
                    NodeHelpers.ToSourceLocation(Node.Args[1].Range));
            }
            else
            {
                var variable = Converter.ConvertValue(Node.Args[0], Scope);
                var address = variable.CreateAddressOfExpression(
                    Scope,
                    NodeHelpers.ToSourceLocation(Node.Args[0].Range))
                    .ResultOrLog(Scope.Log);
                var addressType = address.Type;
                if (addressType.GetIsPointer())
                {
                    return new ReinterpretCastExpression(
                        address,
                        addressType.AsPointerType().ElementType.MakePointerType(
                            PointerKind.TransientPointer));
                }
                else
                {
                    return address;
                }
            }
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
            // TODO: evaluate the LHS only once. The spec says:
            //
            //     The term "evaluated only once" means that in the evaluation of `x op y`, the
            //     results of any constituent expressions of `x` are temporarily saved and then
            //     reused when performing the assignment to `x`. For example, in the assignment
            //     `A()[B()] += C()`, where `A` is a method returning `int[]`, and `B` and `C`
            //     are methods returning `int`, the methods are invoked only once, in the order
            //     `A`, `B`, `C`.

            return (node, scope, conv) =>
            {
                if (!NodeHelpers.CheckArity(node, 2, scope.Log))
                    return ErrorTypeExpression;

                var lhs = conv.ConvertValue(node.Args[0], scope);
                var rhs = conv.ConvertValue(node.Args[1], scope);

                var leftLoc = NodeHelpers.ToSourceLocation(node.Args[0].Range);
                var rightLoc = NodeHelpers.ToSourceLocation(node.Args[1].Range);

                var result = CreateCompoundBinary(
                    Op, lhs, rhs, scope.Function, leftLoc, rightLoc);

                return CreateUncheckedAssignment(
                    lhs, scope.Function.ConvertImplicit(
                        result, lhs.Type, rightLoc),
                    scope, leftLoc.Concat(rightLoc));
            };
        }

        /// <summary>
        /// Applies a binary operator to two values within the context
        /// of a compound assignment.
        /// </summary>
        /// <param name="Op">The operator to apply.</param>
        /// <param name="Left">The left-hand side operand.</param>
        /// <param name="Right">The right-hand side operand.</param>
        /// <param name="Scope">The function scope in which the binary operator application is analyzed.</param>
        /// <param name="LeftLocation">The source location of the left-hand side operand.</param>
        /// <param name="RightLocation">The source location of the right-hand side operand.</param>
        /// <returns>An expression that represents the operator applied to the operands.</returns>
        private static IExpression CreateCompoundBinary(
            Operator Op,
            IValue Left,
            IValue Right,
            FunctionScope Scope,
            SourceLocation LeftLocation,
            SourceLocation RightLocation)
        {
            // You'd think that `x op= y` is just syntactic sugar for `x = x op y`.
            // But that's not exactly true.
            //
            // The C# spec states the following
            //
            //     An operation of the form `x op= y` is processed by applying binary operator
            //     overload resolution as if the operation was written `x op y`. Then,
            //
            //     *  If the return type of the selected operator is implicitly convertible to
            //        the type of `x`, the operation is evaluated as `x = x op y`, except that `x`
            //        is evaluated only once.
            //
            //     *  Otherwise, if the selected operator is a predefined operator, if the return
            //        type of the selected operator is explicitly convertible to the type of `x`,
            //        and if `y` is implicitly convertible to the type of `x` or the operator is a
            //        shift operator, then the operation is evaluated as `x = (T)(x op y)`, where
            //        `T` is the type of `x`, except that `x` is evaluated only once.
            //
            //     *  Otherwise, the compound assignment is invalid, and a binding-time error
            //        occurs.
            //
            // So `byte b = 1; b |= 0x80;` is legal but `byte b = 1; b = b | 0x80;` is not.
            //
            // We'll handle the special case of the second bullet here and defer to the
            // binary operator resolution mechanism for everything else.

            var lhsExpr = Left.CreateGetExpressionOrError(Scope, LeftLocation);
            var rhsExpr = Right.CreateGetExpressionOrError(Scope, RightLocation);

            var lhsType = Left.Type;
            if (Op.Equals(Operator.LeftShift)
                || Op.Equals(Operator.RightShift)
                || Scope.HasImplicitConversion(rhsExpr, lhsType))
            {
                IType opTy;
                if (BinaryOperatorResolution.TryGetPrimitiveOperatorType(Op, Left.Type, Right.Type, out opTy)
                    && opTy != null && Scope.HasExplicitConversion(opTy, lhsType))
                {
                    return Scope.ConvertExplicit(
                        CreatePrimitiveBinaryExpression(
                            Op, lhsExpr, rhsExpr, opTy, Scope, LeftLocation, RightLocation),
                        lhsType,
                        LeftLocation.Concat(RightLocation));
                }
            }

            return CreateBinary(Op, Left, Right, Scope, LeftLocation, RightLocation);
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
                            NodeHelpers.HighlightEven(
                                "cannot resolve variable type '",
                                NodeHelpers.PrintTypeNode(Node),
                                "'."),
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
        /// Converts a switch-statement node (type #switch),
        /// and wraps it in a void expression.
        /// </summary>
        public static IExpression ConvertSwitchExpression(LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckArity(Node, 2, Scope.Log))
                return ErrorTypeExpression;

            var breakTag = new UniqueTag("switch");
            var switchSrcLoc = NodeHelpers.ToSourceLocation(Node.Args[0].Range);
            var switchExpr = Converter.ConvertExpression(Node.Args[0], Scope);
            var switchVar = new RegisterVariable(switchExpr.Type);

            var builder = new SwitchBuilder(
                new VariableValue(switchVar),
                switchSrcLoc,
                new LocalScope(
                    new FlowScope(
                        Scope,
                        new LocalFlow(breakTag, Scope.Flow.ContinueTag))),
                Converter);

            foreach (var childNode in Node.Args[1].Args)
            {
                if (childNode.Calls(CodeSymbols.Case))
                {
                    if (!NodeHelpers.CheckArity(childNode, 1, Scope.Log))
                        continue;

                    builder.AppendCondition(childNode.Args[0]);
                }
                else if (childNode.Calls(CodeSymbols.Label, 1) && childNode.Args[0].IsIdNamed(CodeSymbols.Default))
                {
                    builder.AppendDefault(childNode);
                }
                else
                {
                    builder.AppendStatement(childNode);
                }
            }
            return ToExpression(
                new TaggedStatement(
                    breakTag,
                    new BlockStatement(new IStatement[]
                    {
                        switchVar.CreateSetStatement(switchExpr),
                        builder.FinishSwitch()
                    })));
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

            if (Node.Attrs.Any(item => item.IsIdNamed(CodeSymbols.Yield)))
            {
                // We stumbled across a yield break, which is totally different
                // from a regular yield.
                if (!Scope.Function.ReturnType.GetIsEnumerableType())
                {
                    Scope.Log.LogError(new LogEntry(
                        "syntax error",
                        NodeHelpers.HighlightEven(
                            "methods cannot use ", "yield break",
                            " unless they have an enumerable return type."),
                        NodeHelpers.ToSourceLocation(Node.Range)));
                }
                
                return ToExpression(new YieldBreakStatement());
            }

            var flow = Scope.Flow;
            if (flow.BreakTag == null)
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
                return ToExpression(new BreakStatement(flow.BreakTag));
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

            var flow = Scope.Flow;
            if (flow.ContinueTag == null)
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
                return ToExpression(new ContinueStatement(flow.ContinueTag));
            }
        }

        /// <summary>
        /// Converts a goto statement (type #goto) and wraps it in an expression.
        /// </summary>
        public static IExpression ConvertGotoExpression(
            LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckArity(Node, 1, Scope.Log))
            {
                return ErrorTypeExpression;
            }

            var label = Node.Args[0];

            if (!NodeHelpers.CheckId(label, Scope.Log))
            {
                return ErrorTypeExpression;
            }

            return ToExpression(
                Scope.Function.Labels.CreateGotoStatement(
                    label.Name,
                    NodeHelpers.ToSourceLocation(label.Range)));
        }

        /// <summary>
        /// Converts a label statement (type #label) and wraps it in an expression.
        /// </summary>
        public static IExpression ConvertLabelExpression(
            LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckArity(Node, 1, Scope.Log))
            {
                return ErrorTypeExpression;
            }

            var label = Node.Args[0];

            if (!NodeHelpers.CheckId(label, Scope.Log))
            {
                return ErrorTypeExpression;
            }

            return ToExpression(
                Scope.Function.Labels.CreateMarkStatement(
                    label.Name,
                    NodeHelpers.ToSourceLocation(label.Range),
                    Scope.Log));
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
            var body = Converter.ConvertScopedStatement(
                Node.Args[1], new FlowScope(Scope, new LocalFlow(tag, tag)));

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

            var body = Converter.ConvertScopedStatement(
                Node.Args[0], new FlowScope(Scope, new LocalFlow(tag, tag)));
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
            var cond = Node.Args[1].IsIdNamed(GSymbol.Empty)
                ? new BooleanExpression(true)
                : Converter.ConvertExpression(Node.Args[1], childScope, PrimitiveTypes.Boolean);
            var delta = Converter.ConvertStatementBlock(Node.Args[2].Args, childScope);
            var body = Converter.ConvertScopedStatement(
                Node.Args[3], new FlowScope(childScope, new LocalFlow(tag, tag)));

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
        /// Converts a sizeof node (type #sizeof).
        /// </summary>
        public static IExpression ConvertSizeofExpression(
            LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckArity(Node, 1, Scope.Log))
                return ErrorTypeExpression;

            var ty = Converter.ConvertCheckedType(Node.Args[0], Scope);

            if (ty == null)
            {
                return new UnknownExpression(PrimitiveTypes.Int32);
            }
            else
            {
                return new SizeOfExpression(ty);
            }
        }

        /// <summary>
        /// Converts a nameof node (type #nameof).
        /// </summary>
        public static IExpression ConvertNameofExpression(
            LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckArity(Node, 1, Scope.Log))
            {
                return ErrorTypeExpression;
            }

            // Spec says the following about nameof:
            //
            // The argument to nameof must be a simple name, qualified name, member access,
            // base access with a specified member, or this access with a specified member.
            // The argument expression identifies a code definition, but it is never evaluated.

            var argNode = Node.Args[0];

            if (argNode.Calls(CodeSymbols.Dot, 2) && argNode.Args[1].IsId)
            {
                var simpleName = argNode.Args[1].Name;
                var lhs = Converter.ConvertTypeOrExpression(argNode.Args[0], Scope);
                var lhsType = TypeOrExpressionToType(
                    lhs, Scope, NodeHelpers.ToSourceLocation(argNode.Range));

                if (lhsType != ErrorType.Instance)
                {
                    var anyMatchingMembers =
                        Scope.Function.GetInstanceMembers(lhsType, simpleName.Name)
                        .Concat(Scope.Function.GetStaticMembers(lhsType, simpleName.Name))
                        .Any();

                    if (!anyMatchingMembers)
                    {
                        Scope.Log.LogError(new LogEntry(
                            "syntax error",
                            NodeHelpers.HighlightEven(
                                "type '", Scope.Function.Global.NameAbbreviatedType(lhsType),
                                "' does not define an accessible member with name '", simpleName.Name,
                                "'."),
                            NodeHelpers.ToSourceLocation(argNode.Range)));
                    }
                }
                return new StringExpression(simpleName.Name);
            }
            else if (argNode.IsId)
            {
                var simpleName = argNode.Name;
                TypeOrExpressionToType(
                    Converter.ConvertTypeOrExpression(argNode, Scope),
                    Scope,
                    NodeHelpers.ToSourceLocation(argNode.Range));
                return new StringExpression(simpleName.Name);
            }
            else
            {
                Scope.Log.LogError(new LogEntry(
                    "syntax error",
                    NodeHelpers.HighlightEven(
                        "a '", "nameof",
                        "' expression must take a simple name, " +
                        "qualified name or member access expression as argument."),
                    NodeHelpers.ToSourceLocation(argNode.Range)));
                return new UnknownExpression(PrimitiveTypes.String);
            }
        }

        private static IType TypeOrExpressionToType(
            TypeOrExpression Value,
            LocalScope Scope,
            SourceLocation Location)
        {
            if (Value.IsExpression && !Value.IsType)
            {
                return Value.Expression.CreateGetExpressionOrError(
                    Scope, Location).Type;
            }
            else if (Value.IsType)
            {
                return Value.CollapseTypes(Location, Scope.Function.Global)
                    ?? ErrorType.Instance;
            }
            else
            {
                Scope.Log.LogError(new LogEntry(
                    "expression resolution",
                    NodeHelpers.HighlightEven(
                        "expression is neither a value nor a type."),
                    Location));
                return ErrorType.Instance;
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
            var ty = Converter.ConvertCheckedType(Node.Args[1], Scope);

            if (ty == null)
            {
                return ErrorTypeExpression;
            }

            // In an operation of the form E as T, E must be an expression and T must be a
            // reference type, a type parameter known to be a reference type, or a nullable type.
            if (ty.GetIsPointer())
            {
                // Spec doesn't cover pointers.
                Scope.Log.LogError(new LogEntry(
                    "invalid expression",
                    NodeHelpers.HighlightEven(
                        "the '", "as", "' operator cannot be applied to an operand of pointer type '",
                        Scope.Function.Global.NameAbbreviatedType(ty), "'."),
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
                        Scope.Function.Global.NameAbbreviatedType(ty), "'."),
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
                            Scope.Function.Global.NameAbbreviatedType(ty), "'. Consider adding '",
                            "class", "' or a reference type constraint."),
                        NodeHelpers.ToSourceLocation(Node.Range)));
                }
                else
                {
                    Scope.Log.LogError(new LogEntry(
                        "invalid expression",
                        NodeHelpers.HighlightEven(
                            "the '", "as", "' operator cannot be used with non-nullable value type '",
                            Scope.Function.Global.NameAbbreviatedType(ty), "'."),
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

            if (!Scope.Function.HasReferenceConversion(op.Type, ty))
            {
                Scope.Log.LogError(new LogEntry(
                    "invalid expression",
                    new MarkupNode[]
                    {
                        new MarkupNode(
                            NodeConstants.TextNodeType,
                            "there is no legal conversion from type "),
                        Scope.Function.RenderConversion(op.Type, ty),
                        new MarkupNode(NodeConstants.TextNodeType, "."),
                    },
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
            var ty = Converter.ConvertCheckedType(Node.Args[1], Scope);

            if (ty == null)
            {
                return ErrorTypeExpression;
            }

            var result = new IsExpression(op, ty);

            if (ty.GetIsStaticType())
            {
                Scope.Log.LogError(new LogEntry(
                    "invalid expression",
                    NodeHelpers.HighlightEven(
                        "the second operand of an '", "is",
                        "' operator cannot be '", "static", "' type '",
                        Scope.Function.Global.NameAbbreviatedType(ty), "'."),
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
                        (Scope.Function.Global.NameAbbreviatedType(opType.GetIsPointer() ? opType : ty)), "'."),
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
            var ty = Converter.ConvertCheckedType(Node.Args[1], Scope);

            if (ty == null)
            {
                return ErrorTypeExpression;
            }

            return Scope.Function.ConvertExplicit(
                op, ty, NodeHelpers.ToSourceLocation(Node.Range));
        }

        /// <summary>
        /// Converts a using-cast expression node (type #usingCast).
        /// </summary>
        public static IValue ConvertUsingCastExpression(
            LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            // A using-cast has the same meaning as an implicit cast.
            // Syntax:
            //
            //      x using T
            //

            if (!NodeHelpers.CheckArity(Node, 2, Scope.Log))
                return new ExpressionValue(ErrorTypeExpression);

            // Don't log a warning here. Instead, we'll log a warning
            // when `x using T` is used in a context where `using` cast
            // semantics differ from `(T)x` semantics.
            // This is the case when calling a method on `x using T`
            // where `x` is a struct and `T` is not.

            var op = Converter.ConvertValue(Node.Args[0], Scope);
            var ty = Converter.ConvertCheckedType(Node.Args[1], Scope);

            if (ty == null)
            {
                return new ExpressionValue(ErrorTypeExpression);
            }

            var expr = op.CreateGetExpressionOrError(Scope, NodeHelpers.ToSourceLocation(Node.Range));
            var implConv = Scope.Function.GetImplicitConversion(
                expr, ty, NodeHelpers.ToSourceLocation(Node.Range));

            if (implConv.IsBoxing)
                // Create a special using-box expression node here,
                // which we can recognize when analyzing
                // method groups.
                return new UsingBoxValue(op, ty);
            else
                return new ExpressionValue(implConv.Convert(expr, ty));
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
                    Scope.Function.Global.NameAbbreviatedType(Target.ExpressionType),
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
                Scope,
                Location,
                Location);

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

            // Now, build an IR tree that is equivalent to this:
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
        /// Converts a root type expression, i.e., #object.
        /// </summary>
        public static IType ConvertRootType(LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            var rootTy = Scope.Function.Global.Environment.RootType;
            if (rootTy != null)
            {
                return rootTy;
            }
            else
            {
                Scope.Log.LogError(new LogEntry(
                    "no root type",
                    NodeHelpers.HighlightEven(
                        "type '", "object", "' cannot be resolved because the " +
                        "environment does not define a root type."),
                    NodeHelpers.ToSourceLocation(Node.Range)));
                return null;
            }
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
                        "' had type '", Scope.Function.Global.NameAbbreviatedType(result.Type),
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
        /// Converts a builtin reference-to-pointer expression (type #builtin_ref_to_ptr).
        /// </summary>
        public static IExpression ConvertBuiltinRefToPtrExpression(LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckArity(Node, 1, Scope.Log))
                return ErrorTypeExpression;

            var expr = Converter.ConvertExpression(Node.Args[0], Scope);
            var exprType = expr.Type;
            if (!exprType.GetIsReferenceType()
                || ConversionExpression.Instance.IsNonBoxPointer(exprType))
            {
                Scope.Log.LogError(
                    new LogEntry(
                        "type error",
                        NodeHelpers.HighlightEven(
                            "type '",
                            Scope.Function.Global.NameAbbreviatedType(exprType),
                            "' is not a reference type."),
                        NodeHelpers.ToSourceLocation(Node.Range)));
                return ErrorTypeExpression;
            }
            else
            {
                return new ReinterpretCastExpression(
                    expr,
                    PrimitiveTypes.Void.MakePointerType(PointerKind.TransientPointer));
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
        /// Converts a builtin delegate type node (type #builtin_delegate_type).
        /// </summary>
        public static IType ConvertBuiltinDelegateType(LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckMinArity(Node, 1, Scope.Log))
            {
                return null;
            }

            var types = Node.Args
                .Select(n => Converter.ConvertCheckedTypeOrError(n, Scope))
                .ToArray();

            var signature = new DescribedMethod("", null);
            signature.IsStatic = true;
            signature.ReturnType = types[types.Length - 1];
            for (int i = 0; i < types.Length - 1; i++)
            {
                signature.AddParameter(new DescribedParameter("param" + i, types[i]));
            }

            return MethodType.Create(signature);
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
