using System;
using System.Collections.Generic;
using System.Linq;
using Flame.Compiler;
using Flame.Compiler.Expressions;
using Flame.Compiler.Statements;
using Loyc;
using Loyc.Syntax;
using Pixie;
using Flame.Build;
using Flame.Ecs.Semantics;

namespace Flame.Ecs
{
    /// <summary>
    /// A collection of helper functions that dead with the 
    /// function overload resolution process.
    /// </summary>
    public static class OverloadResolution
    {
        /// <summary>
        /// Gets the argument types for the given list
        /// of arguments.
        /// </summary>
        public static IType[] GetArgumentTypes(
            IReadOnlyList<Tuple<IExpression, SourceLocation>> Arguments)
        {
            var results = new IType[Arguments.Count];
            for (int i = 0; i < results.Length; i++)
            {
                results[i] = Arguments[i].Item1.Type;
            }
            return results;
        }

        /// <summary>
        /// Analyzes the given sequence of argument nodes.
        /// </summary>
        public static IReadOnlyList<Tuple<IExpression, SourceLocation>> ConvertArguments(
            IEnumerable<LNode> ArgumentNodes, LocalScope Scope, NodeConverter Converter)
        {
            return ArgumentNodes.Select(item =>
            {
                var argVal = Converter.ConvertValue(item, Scope);
                var srcLoc = NodeHelpers.ToSourceLocation(item.Range);
                foreach (var attr in item.Attrs)
                {
                    if (attr.IsIdNamed(CodeSymbols.Ref) || attr.IsIdNamed(CodeSymbols.Out))
                    {
                        var argAddr = argVal.CreateAddressOfExpression(Scope, srcLoc);
                        if (!argAddr.IsError)
                        {
                            return Tuple.Create(argAddr.Result, srcLoc);
                        }
                        else
                        {
                            Scope.Log.LogError(new LogEntry(
                                "syntax error", 
                                NodeHelpers.HighlightEven(
                                    "a '", "ref", "' or '", "out", 
                                    "' argument must be a variable with an address. "), 
                                srcLoc));
                            return Tuple.Create<IExpression, SourceLocation>(
                                new UnknownExpression(argVal.Type.MakePointerType(PointerKind.ReferencePointer)),
                                srcLoc);
                        }
                    }
                }
                return Tuple.Create(
                    argVal.CreateGetExpressionOrError(Scope, srcLoc), srcLoc);
            }).ToArray();
        }

        /// <summary>
        /// Tries to create an expression that invokes the most
        /// appropriate delegate in the given candidate expressions 
        /// with the given list of arguments, for the given scope. 
        /// Null is returned if no candidate
        /// is applicable, or if this operation is ambiguous.
        /// </summary>
        public static IExpression CreateUncheckedInvocation(
            IEnumerable<IExpression> Candidates, 
            IReadOnlyList<Tuple<IExpression, SourceLocation>> Arguments,
            FunctionScope Scope)
        {
            return CreateUncheckedInvocation(
                Candidates, Arguments, Scope, 
                GetArgumentTypes(Arguments));
        }

        /// <summary>
        /// Gets the list of parameter types for the given delegate
        /// expression.
        /// </summary>
        /// <returns>The delegate parameter type list.</returns>
        /// <param name="Delegate">The delegate whose parameter type list is to be ascertained.</param>
        private static IReadOnlyList<IType> GetDelegateParameterTypeList(
            IExpression Delegate)
        {
            return Delegate.GetDelegateParameterTypes().ToArray();
        }

        /// <summary>
        /// Tries to create an expression that invokes the most
        /// appropriate delegate in the given candidate expressions 
        /// with the given list of arguments, for the given scope. 
        /// Null is returned if no candidate
        /// is applicable, or if this operation is ambiguous.
        /// The argument types are given as an array, and speed up
        /// this function.
        /// </summary>
        internal static IExpression CreateUncheckedInvocation(
            IEnumerable<IExpression> Candidates, 
            IReadOnlyList<Tuple<IExpression, SourceLocation>> Arguments,
            FunctionScope Scope,
            IType[] ArgumentTypes)
        {
            var argExprs = Arguments.Select(pair => pair.Item1).ToArray();

            IExpression bestDelegate;
            if (!TryResolveOverload(
                Candidates, argExprs, Scope, 
                GetDelegateParameterTypeList,
                out bestDelegate))
            {
                return null;
            }

            var delegateParams = GetDelegateParameterTypeList(bestDelegate);

            var delegateArgs = new IExpression[Arguments.Count];
            for (int i = 0; i < delegateArgs.Length; i++)
            {
                delegateArgs[i] = Scope.ConvertImplicit(
                    Arguments[i].Item1, delegateParams[i], 
                    Arguments[i].Item2);
            }

            return bestDelegate.CreateDelegateInvocationExpression(delegateArgs);
        }

        /// <summary>
        /// Tries to resolve the best overload from the specified set 
        /// of overloads, for a given argument list and scope. 
        /// </summary>
        /// <returns><c>true</c>, if a unique best overload was found, <c>false</c> otherwise.</returns>
        /// <param name="Overloads">The set of possible overloads.</param>
        /// <param name="ArgumentList">The argument list.</param>
        /// <param name="Scope">The current scope.</param>
        /// <param name="GetParameterTypes">Gets the list of parameter types for a given overload.</param>
        /// <param name="Result">The location to which the result will be written.</param>
        /// <typeparam name="T">The type of the overloads.</typeparam>
        public static bool TryResolveOverload<T>(
            IEnumerable<T> Overloads,
            IReadOnlyList<IExpression> ArgumentList,
            FunctionScope Scope,
            Func<T, IReadOnlyList<IType>> GetParameterTypes,
            out T Result)
        {
            // According to the C# spec:
            //
            // Once the candidate function members and the argument list have 
            // been identified, the selection of the best function member is 
            // the same in all cases:
            //
            //     Given the set of applicable candidate function members, 
            //     the best function member in that set is located. If the 
            //     set contains only one function member, then that function 
            //     member is the best function member. Otherwise, the best 
            //     function member is the one function member that is better 
            //     than all other function members with respect to the given 
            //     argument list, provided that each function member is compared 
            //     to all other function members using the rules in Better function 
            //     member. If there is not exactly one function member that 
            //     is better than all other function members, then the function 
            //     member invocation is ambiguous and a binding-time error occurs.

            var candidates = new List<KeyValuePair<T, IReadOnlyList<IType>>>();
            foreach (var item in Overloads)
            {
                var pTypes = GetParameterTypes(item);
                if (IsApplicable(pTypes, ArgumentList, Scope))
                {
                    // Only add items to the candidate set if they
                    // are actually applicable.
                    candidates.Add(
                        new KeyValuePair<T, IReadOnlyList<IType>>(
                            item, pTypes));
                }
            }

            if (candidates.Count == 1)
            {
                // This branch is technically redundant, but it does
                // optimize the common case where a function is not
                // overloaded at all.
                Result = candidates[0].Key;
                return true;
            }

            KeyValuePair<T, IReadOnlyList<IType>> bestOverload;
            bool success = candidates.TryGetBestElement((left, right) => 
                GetBetterFunctionMember(ArgumentList, left.Value, right.Value, Scope), 
                out bestOverload);
            Result = bestOverload.Key;
            return success;
        }

        /// <summary>
        /// Determines if a function form with the given list of parameters is
        /// applicable to the specified argument list within the current scope.
        /// </summary>
        /// <returns>
        /// <c>true</c> if a function form with the given list of parameters is
        /// applicable to the specified argument list within the current scope; otherwise, <c>false</c>.
        /// </returns>
        /// <param name="ParameterTypes">The function form's list of types.</param>
        /// <param name="Arguments">The argument list.</param>
        /// <param name="Scope">The current scope.</param>
        private static bool IsApplicable(
            IReadOnlyList<IType> ParameterTypes, IReadOnlyList<IExpression> Arguments,
            FunctionScope Scope)
        {
            // C# spec dixit:
            //
            // #### Applicable function member
            //
            // A function member is said to be an ***applicable function member*** with 
            // respect to an argument list `A` when all of the following are true:
            //
            // *  Each argument in `A` corresponds to a parameter in the function member 
            //    declaration as described in [Corresponding parameters](expressions.md#corresponding-parameters), 
            //    and any parameter to which no argument corresponds is an optional parameter.
            // *  For each argument in `A`, the parameter passing mode of the argument 
            //    (i.e., value, `ref`, or `out`) is identical to the parameter passing mode 
            //    of the corresponding parameter, and
            //     *  for a value parameter or a parameter array, an implicit conversion 
            //        exists from the argument to the type of the corresponding parameter, or
            //     *  for a `ref` or `out` parameter, the type of the argument is identical 
            //        to the type of the corresponding parameter. After all, a `ref` or `out` 
            //        parameter is an alias for the argument passed.
            //
            // For a function member that includes a parameter array, if the function member 
            // is applicable by the above rules, it is said to be applicable in its 
            // ***normal form***. If a function member that includes a parameter array is not 
            // applicable in its normal form, the function member may instead be applicable 
            // in its ***expanded form***:
            // 
            //     *  The expanded form is constructed by replacing the parameter array in the 
            //        function member declaration with zero or more value parameters of the 
            //        element type of the parameter array such that the number of arguments in 
            //        the argument list `A` matches the total number of parameters. If `A` has 
            //        fewer arguments than the number of fixed parameters in the function member 
            //        declaration, the expanded form of the function member cannot be constructed 
            //        and is thus not applicable.
            //     *  Otherwise, the expanded form is applicable if for each argument in `A` the 
            //        parameter passing mode of the argument is identical to the parameter passing 
            //        mode of the corresponding parameter, and
            //         *  for a fixed value parameter or a value parameter created by the expansion, 
            //            an implicit conversion exists from the type of the argument to the type of 
            //            the corresponding parameter, or
            //         *  for a `ref` or `out` parameter, the type of the argument is identical to 
            //            the type of the corresponding parameter.
            //
            // This function simply figures out some function form with the given list of parameters is
            // applicable to the given list of arguments.
            //
            // TODO: add support for optional parameters.

            if (ParameterTypes.Count != Arguments.Count)
                return false;

            for (int i = 0; i < ParameterTypes.Count; i++)
            {
                if (!Scope.HasImplicitConversion(Arguments[i], ParameterTypes[i]))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Determines which function member is better for the given 
        /// argument list in the current scope.
        /// </summary>
        /// <returns>The better function member.</returns>
        /// <param name="ArgumentList">The argument list.</param>
        /// <param name="FirstParameterTypes">The first list of parameter types.</param>
        /// <param name="SecondParameterTypes">The second list of parameter types.</param>
        /// <param name="Scope">The current scope.</param>
        private static Betterness GetBetterFunctionMember(
            IReadOnlyList<IExpression> ArgumentList,
            IReadOnlyList<IType> FirstParameterTypes,
            IReadOnlyList<IType> SecondParameterTypes,
            FunctionScope Scope)
        {
            if (IsBetterFunctionMember(
                ArgumentList, FirstParameterTypes,
                SecondParameterTypes, Scope))
            {
                return Betterness.First;
            }
            else if (IsBetterFunctionMember(
                ArgumentList, SecondParameterTypes,
                FirstParameterTypes, Scope))
            {
                return Betterness.Second;
            }
            else
            {
                return Betterness.Neither;
            }
        }

        /// <summary>
        /// Determines if the function form with the first list of parameter types 
        /// is a better function member than the function form with the second 
        /// list of parameter types for the specified argument list in the current scope.
        /// </summary>
        /// <returns>
        /// <c>true</c> if the function form with the first list of parameter types 
        /// is a better function member than the function form with the second 
        /// list of parameter types for the specified argument list in the current scope; 
        /// otherwise, <c>false</c>.
        /// </returns>
        /// <param name="ArgumentList">The argument list list.</param>
        /// <param name="BetterParameterTypes">The first list of parameter types.</param>
        /// <param name="WorseParameterTypes">The second list of parameter types.</param>
        /// <param name="Scope">The current scope.</param>
        private static bool IsBetterFunctionMember(
            IReadOnlyList<IExpression> ArgumentList,
            IReadOnlyList<IType> BetterParameterTypes,
            IReadOnlyList<IType> WorseParameterTypes,
            FunctionScope Scope)
        {
            // The C# spec says:
            //
            // Given an argument list A with a set of argument expressions {E1, E2, ..., En} and two 
            // applicable function members Mp and Mq with parameter types {P1, P2, ..., Pn} and 
            // {Q1, Q2, ..., Qn}, Mp is defined to be a better function member than Mq if
            // 
            // *  for each argument, the implicit conversion from Ex to Qx is not better than the 
            //    implicit conversion from Ex to Px, and
            // *  for at least one argument, the conversion from Ex to Px is better than the 
            //    conversion from Ex to Qx.
            // 
            // When performing this evaluation, if Mp or Mq is applicable in its expanded form, then 
            // Px or Qx refers to a parameter in the expanded form of the parameter list.

            bool foundBetter = false;
            for (int i = 0; i < ArgumentList.Count; i++)
            {
                var arg = ArgumentList[i];
                var betterParamTy = BetterParameterTypes[i];
                var worseParamTy = WorseParameterTypes[i];
                if (!foundBetter && IsBetterConversion(arg, betterParamTy, worseParamTy, Scope))
                    foundBetter = true;
                else if (IsBetterConversion(arg, worseParamTy, betterParamTy, Scope))
                    return false;                    
            }
            return foundBetter;
        }

        /// <summary>
        /// Determines if the conversion from the given expression to the first
        /// parameter type is better than the conversion from said expression to
        /// the second parameter type.
        /// </summary>
        /// <returns>
        /// <c>true</c> if is better conversion the specified Argument BetterParameterType WorseParameterType;
        /// otherwise, <c>false</c>.
        /// </returns>
        /// <param name="Argument">The argument.</param>
        /// <param name="BetterParameterType">The first parameter type.</param>
        /// <param name="WorseParameterType">The second parameter type.</param>
        /// <param name="Scope">The current scope.</param> 
        private static bool IsBetterConversion(
            IExpression Argument, 
            IType BetterParameterType, IType WorseParameterType,
            FunctionScope Scope)
        {
            // According to the C# spec:
            //
            // #### Better conversion from expression
            //
            // Given an implicit conversion C1 that converts from an expression E to a type T1, 
            // and an implicit conversion C2 that converts from an expression E to a type T2, 
            // C1 is a better conversion than C2 if E does not exactly match T2 and at least 
            // one of the following holds:
            //
            // *  E exactly matches T1 (Exactly matching Expression)
            // *  T1 is a better conversion target than T2 (Better conversion target)

            return !IsExactlyMatchingExpression(Argument, WorseParameterType, Scope)
                && (IsExactlyMatchingExpression(Argument, BetterParameterType, Scope)
                    || IsBetterConversionTarget(BetterParameterType, WorseParameterType, Scope));
        }

        /// <summary>
        /// Determines if the given expression is an exactly matching expression
        /// for the specified parameter type within the current scope.
        /// </summary>
        /// <returns>
        /// <c>true</c> if the given expression is an exactly matching expression
        /// for the specified parameter type within the current scope; otherwise, <c>false</c>.
        /// </returns>
        /// <param name="Expression">The expression.</param>
        /// <param name="ParameterType">The parameter type.</param>
        /// <param name="Scope">The current scope.</param>
        private static bool IsExactlyMatchingExpression(
            IExpression Expression, IType ParameterType, FunctionScope Scope)
        {
            // Quote from the C# spec:
            //
            // #### Exactly matching Expression
            //
            // Given an expression `E` and a type `T`, `E` exactly matches `T` if one of the 
            // following holds:
            //
            //     *  `E` has a type `S`, and an identity conversion exists from `S` to `T`
            //     *  `E` is an anonymous function, `T` is either a delegate type `D` or an 
            //        expression tree type `Expression<D>` and one of the following holds:
            //         *  An inferred return type `X` exists for `E` in the context of the 
            //            parameter list of `D`, and an identity conversion exists from `X` 
            //            to the return type of `D`
            //         *  Either `E` is non-async and `D` has a return type `Y` or `E` is 
            //            async and `D` has a return type `Task<Y>`, and one of the following 
            //            holds:
            //             * The body of `E` is an expression that exactly matches `Y`
            //             * The body of `E` is a statement block where every return statement
            //               returns an expression that exactly matches `Y`
            //
            // TODO:
            //
            // Implement the bullet about anonymous functions once lambdas are supported.

            foreach (var conv in Scope.ClassifyConversion(Expression, ParameterType))
            {
                if (conv.Kind == ConversionKind.Identity)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Determines if the first type is a better conversion target than
        /// the second type in the current scope.
        /// </summary>
        /// <returns>
        /// <c>true</c> if the first type is a better conversion target than
        /// the second type in the current scope; otherwise, <c>false</c>.
        /// </returns>
        /// <param name="BetterType">The first type.</param>
        /// <param name="WorseType">The second type.</param>
        /// <param name="Scope">The scope.</param>
        private static bool IsBetterConversionTarget(
            IType BetterType, IType WorseType, FunctionScope Scope)
        {
            // The C# spec states:
            //
            // #### Better conversion target
            //
            // Given two different types `T1` and `T2`, `T1` is a better conversion target than 
            // `T2` if no implicit conversion from `T2` to `T1` exists, and at least one of the 
            // following holds:
            //
            //     *  An implicit conversion from `T1` to `T2` exists
            //     *  `T1` is either a delegate type `D1` or an expression tree type `Expression<D1>`, 
            //        `T2` is either a delegate type `D2` or an expression tree type `Expression<D2>`, 
            //        `D1` has a return type `S1` and one of the following holds:
            //         *  `D2` is void returning
            //         *  `D2` has a return type `S2`, and `S1` is a better conversion target than `S2`
            //     *  `T1` is `Task<S1>`, `T2` is `Task<S2>`, and `S1` is a better conversion 
            //        target than `S2`
            //     *  `T1` is `S1` or `S1?` where `S1` is a signed integral type, and `T2` is 
            //        `S2` or `S2?` where `S2` is an unsigned integral type. Specifically:
            //         * `S1` is `sbyte` and `S2` is `byte`, `ushort`, `uint`, or `ulong`
            //         * `S1` is `short` and `S2` is `ushort`, `uint`, or `ulong`
            //         * `S1` is `int` and `S2` is `uint`, or `ulong`
            //         * `S1` is `long` and `S2` is `ulong`
            //
            // TODO:
            //
            // For now, we won't give `Expression<T>` and `Task<T>` special treatment, and we'll 
            // ignore nullable types; all three rely in BCL-specific types and we might want to 
            // avoid tying `ecsc` to a particular standard library. Also, none of these features
            // have been implemented in `ecsc` at the time of writing. This should be revisited 
            // once expression trees, `async` or nullable types have been implemented.

            if (Scope.HasImplicitConversion(WorseType, BetterType))
                return false;

            if (Scope.HasImplicitConversion(BetterType, WorseType))
                return true;

            var betterMethod = MethodType.GetMethod(BetterType);
            if (betterMethod != null)
            {
                var worseMethod = MethodType.GetMethod(WorseType);
                if (worseMethod != null)
                {
                    var worseRetType = betterMethod.ReturnType;
                    if (worseRetType.Equals(PrimitiveTypes.Void))
                        return true;

                    var betterRetType = betterMethod.ReturnType;
                    if (IsBetterConversionTarget(worseRetType, worseRetType, Scope))
                        return true;
                }
            }

            if (BetterType.GetIsSignedInteger()
                && WorseType.GetIsUnsignedInteger())
            {
                return true;
            }

            return false;
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

        internal static IExpression CreateFailedOverloadExpression(
            IExpression Target, IEnumerable<IStatement> ArgStmts, 
            IType ReturnType)
        {
            var innerStmts = new List<IStatement>();
            innerStmts.Add(new ExpressionStatement(Target));
            innerStmts.AddRange(ArgStmts);
            return new InitializedExpression(
                new BlockStatement(innerStmts), new UnknownExpression(ReturnType));
        }

        internal static IExpression CreateFailedOverloadExpression(
            IExpression Target, 
            IEnumerable<Tuple<IExpression, SourceLocation>> Arguments, 
            IType ReturnType)
        {
            return CreateFailedOverloadExpression(
                Target, 
                Arguments.Select(arg => new ExpressionStatement(arg.Item1)), 
                ReturnType);
        }

        /// <summary>
        /// Provides diagnostics for a failed overload.
        /// An expression is returned that executes the
        /// overload's target and arguments.
        /// </summary>
        internal static IExpression LogFailedOverload(
            string FunctionType,
            IEnumerable<IExpression> Candidates,
            IReadOnlyList<Tuple<IExpression, SourceLocation>> Arguments,
            GlobalScope Scope, SourceLocation Location,
            IType[] ArgumentTypes)
        {
            var target = IntersectionExpression.Create(Candidates);

            var matches = target.GetMethodGroup();
            var namer = Scope.TypeNamer;

            var retType = matches.Any() ? matches.First().ReturnType : ErrorType.Instance;
            var expectedSig = CreateExpectedSignatureDescription(namer, retType, ArgumentTypes);

            // Create an inner expression that consists of the invocation's target and arguments,
            // whose values are calculated and then popped. Said expression will return an 
            // unknown value of the return type.
            var innerExpr = CreateFailedOverloadExpression(target, Arguments, retType);

            if (ErrorType.Instance.Equals(target.Type)
                || ArgumentTypes.Contains(ErrorType.Instance))
            {
                // Don't log any diagnostics if either the target
                // type or one of the argument types is the error
                // type, because that means that an error has already been logged.
                // Printing another error on top of that would hide the real
                // problem.
                return innerExpr;
            }

            var log = Scope.Log;
            if (matches.Any())
            {
                var failedMatchesList = matches.Select(
                                            m => CreateSignatureDiff(namer, ArgumentTypes, m));

                var explanationNodes = NodeHelpers.HighlightEven(
                                           FunctionType + " call could not be resolved. " +
                                           "Expected signature compatible with '", expectedSig,
                                           "'. Incompatible or ambiguous matches:");

                var failedMatchesNode = ListExtensions.Instance.CreateList(failedMatchesList);
                log.LogError(new LogEntry(
                    FunctionType + " resolution",
                    explanationNodes.Concat(new MarkupNode[] { failedMatchesNode }),
                    Location));
            }
            else
            {
                // Print an error message if we try to call a non-error expression.
                // Printing error messages for error expression is not that useful, because
                // this may confuse the user about the real error.
                log.LogError(new LogEntry(
                    FunctionType + " resolution",
                    NodeHelpers.HighlightEven(
                        FunctionType + " call could not be resolved because the call's target was not invocable. " +
                        "Expected signature compatible with '", expectedSig,
                        "', got an expression of type '", namer.Convert(target.Type), "'."),
                    Location));
            }
            return innerExpr;
        }

        /// <summary>
        /// Tries to create an expression that invokes the most
        /// appropriate delegate in the list of candidate expressions 
        /// with the given list of arguments, for the given scope. 
        /// Null is never returned: if the operation is impossible 
        /// or ambiguous, then a diagnostic is logged.
        /// </summary>
        public static IExpression CreateCheckedInvocation(
            string InvocationType, IEnumerable<IExpression> Candidates, 
            IReadOnlyList<Tuple<IExpression, SourceLocation>> Arguments,
            FunctionScope Scope, SourceLocation Location)
        {
            var argTypes = OverloadResolution.GetArgumentTypes(Arguments);

            var result = OverloadResolution.CreateUncheckedInvocation(Candidates, Arguments, Scope);

            if (result != null)
            {
                // Awesome! Everything went well.
                return result;
            }
            else
            {
                // Something went wrong. Try to provide accurate diagnostics.
                return OverloadResolution.LogFailedOverload(
                    InvocationType, Candidates, Arguments, Scope.Global, 
                    Location, argTypes);
            }
        }

        /// <summary>
        /// Creates a new-object creation delegate expression
        /// from the given constructor.
        /// </summary>
        public static IExpression ToNewObjectDelegate(IMethod Constructor)
        {
            return new NewObjectDelegate(Constructor);
        }

        /// <summary>
        /// Tries to create an expression that creates a new
        /// object instance, by using the most
        /// appropriate constructor in the candidate list 
        /// with the given list of arguments, for the given scope. 
        /// Null is never returned: if the operation is impossible 
        /// or ambiguous, then a diagnostic is logged.
        /// </summary>
        public static IExpression CreateCheckedNewObject(
            IEnumerable<IMethod> CandidateConstructors, 
            IReadOnlyList<Tuple<IExpression, SourceLocation>> Arguments,
            FunctionScope Scope, SourceLocation Location)
        {
            var delegates = CandidateConstructors.Select(ToNewObjectDelegate).ToArray();

            return CreateCheckedInvocation("new-object", delegates, Arguments, Scope, Location);
        }
    }
}

