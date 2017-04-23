using System;
using System.Collections.Generic;
using Flame.Compiler;
using System.Linq;
using Flame.Compiler.Expressions;

namespace Flame.Ecs.Semantics
{
    /// <summary>
    /// A candidate overload during overload resolution.
    /// </summary>
    public abstract class CandidateOverload
    {
        /// <summary>
        /// Gets a value indicating whether this overload is in expanded form.
        /// </summary>
        /// <value><c>true</c> if this overload is expanded; otherwise, <c>false</c>.</value>
        public abstract bool IsExpanded { get; }

        /// <summary>
        /// Gets the candidate overload's parameter types.
        /// </summary>
        /// <value>The parameter types.</value>
        public abstract IReadOnlyList<IType> ParameterTypes { get; }

        /// <summary>
        /// Creates an invocation expression for the given argument list.
        /// </summary>
        /// <returns>The invocation expression.</returns>
        /// <param name="Arguments">The argument list.</param>
        public abstract IExpression CreateInvocationExpression(
            IReadOnlyList<IExpression> Arguments);

        /// <summary>
        /// Creates all normal and expanded overload candidates for
        /// the given sequence of expressions.
        /// </summary>
        /// <returns>
        /// The list of all normal and expanded overload candidates
        /// for the given sequence of expressions.
        /// </returns>
        /// <param name="CandidateDelegates">
        /// The sequence of candidate (delegate) expressions.
        /// </param>
        /// <param name="ArgumentCount">The number of arguments.</param>
        public static IReadOnlyList<CandidateOverload> CreateAll(
            IEnumerable<IExpression> CandidateDelegates, int ArgumentCount)
        {
            var results = new List<CandidateOverload>();
            foreach (var item in CandidateDelegates)
            {
                NormalFormOverload normal;
                if (NormalFormOverload.TryCreate(item, out normal))
                {
                    results.Add(normal);
                    ExpandedFormOverload expanded;
                    if (ExpandedFormOverload.TryCreate(
                        item, ArgumentCount, out expanded))
                    {
                        results.Add(expanded);
                    }
                }
            }
            return results;
        }
    }

    /// <summary>
    /// Represents overloads in normal form.
    /// </summary>
    public sealed class NormalFormOverload : CandidateOverload
    {
        private NormalFormOverload(
            IExpression DelegateExpression, 
            IReadOnlyList<IType> ParameterTypes)
        {
            this.delegateExpr = DelegateExpression;
            this.paramTypeList = ParameterTypes;
        }

        private IExpression delegateExpr;
        private IReadOnlyList<IType> paramTypeList;

        /// <summary>
        /// Gets the delegate expression that backs this normal form overload.
        /// </summary>
        public IExpression DelegateExpression => delegateExpr;

        /// <inheritdoc/>
        public override IReadOnlyList<IType> ParameterTypes { get { return paramTypeList; } }

        /// <inheritdoc/>
        public override bool IsExpanded { get { return false; } }

        /// <inheritdoc/>
        public override IExpression CreateInvocationExpression(
            IReadOnlyList<IExpression> Arguments)
        {
            return delegateExpr.CreateDelegateInvocationExpression(Arguments);
        }

        /// <summary>
        /// Tries to create a normal form overload.
        /// </summary>
        /// <returns><c>true</c>, if a normal form overload was created, <c>false</c> otherwise.</returns>
        /// <param name="DelegateExpression">The delegate expression on which the overload is based.</param>
        /// <param name="Result">The normal form overload.</param>
        public static bool TryCreate(
            IExpression DelegateExpression, 
            out NormalFormOverload Result)
        {
            var paramTypeList = DelegateExpression.GetDelegateParameterTypes();
            if (paramTypeList == null)
            {
                Result = null;
                return false;
            }
            else
            {
                Result = new NormalFormOverload(
                    DelegateExpression, 
                    paramTypeList as IType[] ?? paramTypeList.ToArray());
                return true;
            }
        }
    }

    /// <summary>
    /// Represents overloads in expanded form.
    /// </summary>
    public sealed class ExpandedFormOverload : CandidateOverload
    {
        private ExpandedFormOverload(
            IExpression NormalFormDelegate, 
            IType ExpandedArgumentType,
            IType ExpandedElementType,
            IReadOnlyList<IType> ExpandedFormParameterTypes)
        {
            this.NormalFormDelegate = NormalFormDelegate;
            this.expandedArgumentType = ExpandedArgumentType;
            this.expandedElementType = ExpandedElementType;
            this.expandedFormParameterTypes = ExpandedFormParameterTypes;
        }

        /// <summary>
        /// Gets the normal form delegate.
        /// </summary>
        /// <value>The normal form delegate.</value>
        public IExpression NormalFormDelegate { get; private set; }

        private IType expandedArgumentType;
        private IType expandedElementType;
        private IReadOnlyList<IType> expandedFormParameterTypes;

        /// <inheritdoc/>
        public override bool IsExpanded { get { return true; } }

        /// <inheritdoc/>
        public override IReadOnlyList<IType> ParameterTypes
        {
            get { return expandedFormParameterTypes; }
        }

        /// <inheritdoc/>
        public override IExpression CreateInvocationExpression(
            IReadOnlyList<IExpression> Arguments)
        {
            var argArray = Arguments.ToArray();
            int normalFormCount = MethodType
                .GetMethod(NormalFormDelegate.Type)
                .Parameters.Count() - 1;
            return NormalFormDelegate.CreateDelegateInvocationExpression(
                argArray.Take(normalFormCount)
                .Concat(new IExpression[]
                {
                    new ReinterpretCastExpression(
                        new InitializedArrayExpression(
                            expandedElementType, 
                            argArray.Skip(normalFormCount).ToArray()),
                        expandedArgumentType)
                })
                .ToArray());
        }

        /// <summary>
        /// Creates an overload that calls the given normal-form delegate
        /// in expanded form, if possible.
        /// </summary>
        /// <returns><c>true</c>, if an expanded form overload can be created; otherwise, <c>false</c> otherwise.</returns>
        /// <param name="NormalFormDelegate">The normal form delegate.</param>
        /// <param name="ArgumentCount">The number of arguments in the argument list.</param>
        /// <param name="Result">The expanded form overload.</param>
        public static bool TryCreate(
            IExpression NormalFormDelegate, 
            int ArgumentCount,
            out ExpandedFormOverload Result)
        {
            var normalFormMethod = MethodType.GetMethod(
                NormalFormDelegate.Type);

            if (normalFormMethod == null)
            {
                Result = null;
                return false;
            }

            var paramList = normalFormMethod.Parameters.ToArray();
            if (paramList.Length == 0)
            {
                Result = null;
                return false;
            }

            var lastParam = paramList[paramList.Length - 1];

            if (!lastParam.HasAttribute(
                PrimitiveAttributes.Instance.VarArgsAttribute.AttributeType))
            {
                Result = null;
                return false;
            }

            // The expanded form consists of all normal form parameters
            // except for the last one...
            var expandedParamTypes = new List<IType>();
            for (int i = 0; i < paramList.Length - 1; i++)
            {
                expandedParamTypes.Add(paramList[i].ParameterType);
            }

            // ... followed by the element type of the final parameter,
            // repeated until the expanded form matches the argument list
            // in length. 
            var lastParamType = lastParam.ParameterType;
            var lastParamElemType = lastParamType.GetEnumerableElementType();
            if (lastParamElemType == null)
            {
                // Whoa. This isn't right.
                Result = null;
                return false;
            }

            for (int i = expandedParamTypes.Count; i < ArgumentCount; i++)
            {
                expandedParamTypes.Add(lastParamElemType);
            }

            Result = new ExpandedFormOverload(
                NormalFormDelegate, 
                lastParamType,
                lastParamElemType,
                expandedParamTypes);
            
            return true;
        }
    }
}

