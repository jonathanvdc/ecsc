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
                var argExpr = Converter.ConvertExpression(item, Scope);
                foreach (var attr in item.Attrs)
                {
                    if (attr.IsIdNamed(CodeSymbols.Ref) || attr.IsIdNamed(CodeSymbols.Out))
                    {
                        var argVar = ExpressionConverters.AsVariable(argExpr) as IUnmanagedVariable;
                        if (argVar != null)
                        {
                            argExpr = argVar.CreateAddressOfExpression();
                        }
                        else
                        {
                            Scope.Log.LogError(new LogEntry(
                                "syntax error", 
                                NodeHelpers.HighlightEven(
                                    "a ", "ref", " or ", "out", 
                                    " argument must be an assignable variable."),
                                NodeHelpers.ToSourceLocation(attr.Range)));
                        }
                    }
                }
                return Tuple.Create(argExpr, NodeHelpers.ToSourceLocation(item.Range));
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
            GlobalScope Scope)
        {
            return CreateUncheckedInvocation(
                Candidates, Arguments, Scope, 
                GetArgumentTypes(Arguments));
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
            GlobalScope Scope,
            IType[] ArgumentTypes)
        {
            // TODO: implement and use C#-specific overload resolution
            // here. (i.e. read and implement the language spec)
            var bestDelegate = Candidates.GetBestDelegate(ArgumentTypes);

            if (bestDelegate == null)
                return null;

            var delegateParams = bestDelegate.GetDelegateParameterTypes().ToArray();

            var delegateArgs = new IExpression[Arguments.Count];
            for (int i = 0; i < delegateArgs.Length; i++)
            {
                delegateArgs[i] = Scope.ConvertImplicit(
                    Arguments[i].Item1, delegateParams[i], 
                    Arguments[i].Item2);
            }

            return bestDelegate.CreateDelegateInvocationExpression(delegateArgs);
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

            var retType = matches.Any() ? matches.First().ReturnType : PrimitiveTypes.Void;
            var expectedSig = CreateExpectedSignatureDescription(namer, retType, ArgumentTypes);

            // Create an inner expression that consists of the invocation's target and arguments,
            // whose values are calculated and then popped. Said expression will return an 
            // unknown value of the return type.
            var innerExpr = CreateFailedOverloadExpression(target, Arguments, retType);

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
            GlobalScope Scope, SourceLocation Location)
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
                    InvocationType, Candidates, Arguments, Scope, 
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
            GlobalScope Scope, SourceLocation Location)
        {
            var delegates = CandidateConstructors.Select(ToNewObjectDelegate).ToArray();

            return CreateCheckedInvocation("new-object", delegates, Arguments, Scope, Location);
        }
    }
}

