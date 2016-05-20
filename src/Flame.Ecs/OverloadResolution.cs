using System;
using System.Collections.Generic;
using System.Linq;
using Flame.Compiler;
using Loyc;
using Loyc.Syntax;

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
                                "invalid syntax", 
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
        public static IExpression CreateInvocation(
            IEnumerable<IExpression> Candidates, 
            IReadOnlyList<Tuple<IExpression, SourceLocation>> Arguments,
            GlobalScope Scope)
        {
            return CreateInvocation(
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
        internal static IExpression CreateInvocation(
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
    }
}

