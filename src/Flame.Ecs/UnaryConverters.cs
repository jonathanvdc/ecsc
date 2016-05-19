﻿using System;
using System.Collections.Generic;
using Flame.Compiler;
using Flame.Compiler.Expressions;
using Flame.Compiler.Statements;
using Flame.Compiler.Variables;
using Loyc.Syntax;

namespace Flame.Ecs
{
    public static class UnaryConverters
    {
        // Describes all primitive types that can be used in 
        // the decrement/increment operators.
        private static readonly HashSet<IType> primitiveIncDecTypes = new HashSet<IType>()
        {
            PrimitiveTypes.Int8, PrimitiveTypes.Int16,
            PrimitiveTypes.Int32, PrimitiveTypes.Int64,
            PrimitiveTypes.UInt8, PrimitiveTypes.UInt16,
            PrimitiveTypes.UInt32, PrimitiveTypes.UInt64,
            PrimitiveTypes.Char,
            PrimitiveTypes.Float32, PrimitiveTypes.Float64
        };

        /// <summary>
        /// Creates an expression converter for increment/decrement expressions.
        /// </summary>
        public static Func<LNode, LocalScope, NodeConverter, IExpression> CreateIncDecExpressionConverter(
            string OperatorName, Func<IVariable, IType, IExpression> CreatePrimitiveExpression)
        {
            return (node, scope, converter) =>
            {
                if (!NodeHelpers.CheckArity(node, 1, scope.Log))
                    return VoidExpression.Instance;

                var innerExpr = converter.ConvertExpression(node.Args[0], scope);
                var innerVar = ExpressionConverters.AsVariable(innerExpr);

                var innerTy = innerVar.Type;

                if (primitiveIncDecTypes.Contains(innerTy))
                {
                    return CreatePrimitiveExpression(innerVar, innerTy);
                }
                else
                {
                    // TODO: user-defined operators

                    // Assume that all types that are not eligible primitive types,
                    // cannot be 
                    scope.Log.LogError(new LogEntry(
                        "type error",
                        NodeHelpers.HighlightEven(
                            "the '", OperatorName, "' operator cannot be applied to operand of type '", 
                            scope.Function.Global.TypeNamer.Convert(innerTy), "'."),
                        NodeHelpers.ToSourceLocation(node.Range)));
                    return innerExpr;
                }
            };
        }

        private static IExpression CreateAddOrSubExpression(
            IVariable Variable, IType Type, int Increment)
        {
            if (Increment < 0)
            {
                return new SubtractExpression(
                    Variable.CreateGetExpression(), 
                    new StaticCastExpression(
                        new Int32Expression(-Increment), Type).Optimize());
            }
            else
            {
                return new AddExpression(
                    Variable.CreateGetExpression(), 
                    new StaticCastExpression(
                        new Int32Expression(Increment), Type).Optimize());
            }
        }

        /// <summary>
        /// Creates an expression converter for prefix increment/decrement expressions.
        /// </summary>
        public static Func<LNode, LocalScope, NodeConverter, IExpression> CreatePrefixIncDecConverter(
            string OperatorName, int Increment)
        {
            return CreateIncDecExpressionConverter(OperatorName,
                (variable, ty) =>
                {
                    return ExpressionConverters.CreateUncheckedAssignment(
                        variable,
                        CreateAddOrSubExpression(variable, ty, Increment));
                });
        }

        public static readonly Func<LNode, LocalScope, NodeConverter, IExpression> ConvertPrefixIncrement =
            CreatePrefixIncDecConverter("++", 1);

        public static readonly Func<LNode, LocalScope, NodeConverter, IExpression> ConvertPrefixDecrement = 
            CreatePrefixIncDecConverter("--", -1);

        /// <summary>
        /// Creates a postfix increment/decrement expression.
        /// </summary>
        private static IExpression CreateLocalPostfixIncDecExpr(
            IVariable Variable, IType Type, int Increment)
        {
            // Create the following expression:
            //
            //     {
            //         init: {},
            //         value: Variable.get(),
            //         final: Variable.set(Variable.get() + Increment) 
            //     }

            return new InitializedExpression(
                EmptyStatement.Instance,
                Variable.CreateGetExpression(),
                Variable.CreateSetStatement(
                    CreateAddOrSubExpression(Variable, Type, Increment)));
        }

        /// <summary>
        /// Creates a postfix increment/decrement expression.
        /// </summary>
        private static IExpression CreateGeneralPostfixIncDecExpr(
            IVariable Variable, IType Type, int Increment)
        {
            // Create the following expression:
            //
            //     {
            //         init: var tmp = Variable.get(),
            //         value: tmp.get(),
            //         final: { Variable.set(tmp.get() + Increment), tmp.release() }
            //     }

            var local = new RegisterVariable("tmp", Type);
            return new InitializedExpression(
                local.CreateSetStatement(Variable.CreateGetExpression()),
                local.CreateGetExpression(),
                new BlockStatement(new IStatement[]
                {
                    Variable.CreateSetStatement(
                        CreateAddOrSubExpression(local, Type, Increment)),
                    local.CreateReleaseStatement()
                }));
        }

        /// <summary>
        /// Creates an expression converter for postfix increment/decrement expressions.
        /// </summary>
        public static Func<LNode, LocalScope, NodeConverter, IExpression> CreatePostfixIncDecConverter(
            string OperatorName, int Increment)
        {
            return CreateIncDecExpressionConverter(OperatorName,
                (variable, ty) =>
                {
                    if (ExpressionConverters.IsLocalVariable(variable))
                        return CreateLocalPostfixIncDecExpr(variable, ty, Increment);
                    else
                        return CreateGeneralPostfixIncDecExpr(variable, ty, Increment);
                });
        }

        public static readonly Func<LNode, LocalScope, NodeConverter, IExpression> ConvertPostfixIncrement =
            CreatePostfixIncDecConverter("++", 1);

        public static readonly Func<LNode, LocalScope, NodeConverter, IExpression> ConvertPostfixDecrement = 
            CreatePostfixIncDecConverter("--", -1);
    }
}

