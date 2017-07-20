using System;
using System.Collections.Generic;
using System.Linq;
using Flame.Compiler;
using Flame.Compiler.Expressions;
using Flame.Compiler.Statements;
using Flame.Compiler.Variables;
using Flame.Ecs.Semantics;
using Flame.Ecs.Values;
using Loyc.Syntax;

namespace Flame.Ecs
{
    public static class UnaryConverters
    {
        #region Increment/Decrement

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
            string OperatorName,
            Func<IValue, IType, LocalScope, SourceLocation, IExpression> CreatePrimitiveExpression)
        {
            return (node, scope, converter) =>
            {
                if (!NodeHelpers.CheckArity(node, 1, scope.Log))
                    return VoidExpression.Instance;

                var innerExpr = converter.ConvertValue(node.Args[0], scope);

                var loc = NodeHelpers.ToSourceLocation(node.Range);

                var innerTy = innerExpr.Type;

                if (primitiveIncDecTypes.Contains(innerTy))
                {
                    return CreatePrimitiveExpression(innerExpr, innerTy, scope, loc);
                }
                else if (innerTy.GetIsPointer())
                {
                    var ptrTy = innerTy.AsPointerType();
                    if (ptrTy.PointerKind.Equals(PointerKind.TransientPointer)
                        && !ptrTy.ElementType.Equals(PrimitiveTypes.Void))
                    {
                        return CreatePrimitiveExpression(innerExpr, innerTy, scope, loc);
                    }
                }

                // TODO: user-defined operators
                scope.Log.LogError(new LogEntry(
                    "type error",
                    NodeHelpers.HighlightEven(
                        "the '", OperatorName, "' operator cannot be applied to an operand of type '",
                        scope.Function.Global.NameAbbreviatedType(innerTy), "'."),
                    loc));
                return innerExpr.CreateGetExpressionOrError(scope, loc);
            };
        }

        private static IExpression CreateIncrementRhs(IType Type, int Increment)
        {
            if (Type.GetIsPointer())
            {
                return new IntegerExpression(Increment);
            }
            else
            {
                return new StaticCastExpression(
                    new IntegerExpression(Increment), Type).Simplify();
            }
        }

        private static IExpression CreateAddOrSubExpression(
            IExpression Value, IType Type, int Increment)
        {
            if (Increment < 0)
            {
                return new SubtractExpression(
                    Value,
                    CreateIncrementRhs(Type, -Increment));
            }
            else
            {
                return new AddExpression(
                    Value,
                    CreateIncrementRhs(Type, Increment));
            }
        }

        /// <summary>
        /// Creates an expression converter for prefix increment/decrement expressions.
        /// </summary>
        public static Func<LNode, LocalScope, NodeConverter, IExpression> CreatePrefixIncDecConverter(
            string OperatorName, int Increment)
        {
            return CreateIncDecExpressionConverter(OperatorName,
                (variable, ty, scope, loc) =>
                {
                    return ExpressionConverters.CreateUncheckedAssignment(
                        variable,
                        CreateAddOrSubExpression(
                            variable.CreateGetExpressionOrError(scope, loc),
                            ty, Increment),
                        scope, loc);
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
            IValue Variable, IType Type, int Increment, LocalScope Scope, SourceLocation Location)
        {
            // Create the following expression:
            //
            //     {
            //         init: {},
            //         value: Variable.get(),
            //         final: Variable.set(Variable.get() + Increment) 
            //     }

            var getExpr = Variable.CreateGetExpressionOrError(Scope, Location);
            return new InitializedExpression(
                EmptyStatement.Instance,
                getExpr,
                Variable.CreateSetStatementOrError(
                    CreateAddOrSubExpression(
                        getExpr, Type, Increment),
                    Scope, Location));
        }

        /// <summary>
        /// Creates a postfix increment/decrement expression.
        /// </summary>
        private static IExpression CreateGeneralPostfixIncDecExpr(
            IValue Variable, IType Type, int Increment,
            ILocalScope Scope, SourceLocation Location)
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
                local.CreateSetStatement(Variable.CreateGetExpressionOrError(Scope, Location)),
                local.CreateGetExpression(),
                new BlockStatement(new IStatement[]
                {
                    Variable.CreateSetStatementOrError(
                        CreateAddOrSubExpression(local.CreateGetExpression(), Type, Increment),
                        Scope, Location),
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
                (variable, ty, scope, loc) =>
                {
                    if (ExpressionConverters.IsLocalVariable(variable))
                        return CreateLocalPostfixIncDecExpr(variable, ty, Increment, scope, loc);
                    else
                        return CreateGeneralPostfixIncDecExpr(variable, ty, Increment, scope, loc);
                });
        }

        public static readonly Func<LNode, LocalScope, NodeConverter, IExpression> ConvertPostfixIncrement =
            CreatePostfixIncDecConverter("++", 1);

        public static readonly Func<LNode, LocalScope, NodeConverter, IExpression> ConvertPostfixDecrement =
            CreatePostfixIncDecConverter("--", -1);

        #endregion

        #region Indexing

        /// <summary>
        /// Converts an indexing expression (type @_[]).
        /// </summary>
        public static IValue ConvertIndex(
            LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckMinArity(Node, 2, Scope.Log))
                return new ExpressionValue(ExpressionConverters.ErrorTypeExpression);

            var containerVal = Converter.ConvertValue(Node.Args[0], Scope);
            var dimExprs = OverloadResolution.ConvertArguments(Node.Args.Slice(1), Scope, Converter);

            var containerTy = containerVal.Type;
            var loc = NodeHelpers.ToSourceLocation(Node.Range);

            if (containerTy.GetIsArray() || containerTy.GetIsVector())
            {
                // We will handle built-in container types (arrays, vectors)
                // here, because they are a special case.

                var containerExpr = containerVal.CreateGetExpressionOrError(Scope, loc);

                var elemTy = containerTy.AsContainerType().ElementType;

                int dims = containerTy.GetIsArray()
                    ? containerTy.AsArrayType().ArrayRank
                    : containerTy.AsVectorType().Dimensions.Count;

                if (dims != dimExprs.Count)
                {
                    Scope.Log.LogError(new LogEntry(
                        "syntax error",
                        NodeHelpers.HighlightEven(
                            "wrong number of indexes '", dimExprs.Count.ToString(), "' inside ",
                            "[]", ", expected '", dims.ToString(), "'."),
                        loc));
                    return new ExpressionValue(new UnknownExpression(elemTy));
                }

                foreach (var item in dimExprs)
                {
                    var ty = item.Item1.Type;
                    if (!ty.GetIsInteger() && !PrimitiveTypes.Char.Equals(ty))
                    {
                        Scope.Log.LogError(new LogEntry(
                            "type error",
                            NodeHelpers.HighlightEven(
                                "index inside ", "[]", " of type '", Scope.Function.Global.NameAbbreviatedType(ty),
                                "' was not an integer."),
                            loc));
                    }
                }
                return new VariableValue(new ElementVariable(containerExpr, dimExprs.Select(t => t.Item1)));
            }
            else
            {
                var indexers =
                    Scope.Function.GetInstanceIndexers(containerTy)
                        .Select(
                            p => new IndexerDelegateExpression(
                                p, ExpressionConverters.AsTargetValue(
                                    containerVal, p.DeclaringType, Scope, loc, true).ResultOrLog(Scope.Log)))
                        .ToArray();

                if (indexers.Length == 0)
                {
                    Scope.Log.LogError(new LogEntry(
                        "type error",
                        NodeHelpers.HighlightEven(
                            "cannot apply indexing with '", "[]",
                            "' to an expression of type '",
                            Scope.Function.Global.NameAbbreviatedType(containerTy), "'."),
                        loc));
                    return new ExpressionValue(ExpressionConverters.ErrorTypeExpression);
                }

                var invocation = OverloadResolution.CreateCheckedInvocation(
                    "indexer", indexers, dimExprs,
                    Scope.Function, loc);

                if (invocation is AccessIndexerExpression)
                {
                    var accessExpr = (AccessIndexerExpression)invocation;
                    return new IndexerValue(accessExpr.Property, accessExpr.Target, accessExpr.Arguments);
                }

                return new ExpressionValue(invocation);
            }
        }

        #endregion

        #region Other unary operators

        /// <summary>
        /// Creates a built-in unary operator expression.
        /// </summary>
        private static IExpression CreatePrimitiveUnary(
            Operator Op, IExpression Operand)
        {
            if (Op.Equals(UnaryOperatorResolution.BitwiseComplement))
                // Bitwise complement and logical 'not' are equivalent
                // in Flame.
                return new NotExpression(Operand);
            else if (Op.Equals(Operator.Add))
                // Unary plus doesn't really do anything.
                return Operand;
            else
                return DirectUnaryExpression.Instance.Create(Op, Operand);
        }

        /// <summary>
        /// Creates a unary operator application expression
        /// for the given operator and operand. A scope is
        /// used to perform conversions and log error messages,
        /// and a source location is used to highlight potential
        /// issues.
        /// </summary>
        public static IExpression CreateUnary(
            Operator Op, IExpression Operand,
            FunctionScope Scope,
            SourceLocation Location)
        {
            var ty = Operand.Type;

            IType opTy;
            if (UnaryOperatorResolution.TryGetPrimitiveOperatorType(Op, ty, out opTy))
            {
                if (opTy == null)
                {
                    Scope.Global.Log.LogError(new LogEntry(
                        "operator application",
                        NodeHelpers.HighlightEven(
                            "operator '", Op.Name, "' cannot be applied to an operand of type '",
                            Scope.Global.NameAbbreviatedType(ty), "'."),
                        Location));
                    return new UnknownExpression(ty);
                }

                return CreatePrimitiveUnary(
                    Op, Scope.ConvertImplicit(Operand, opTy, Location));
            }

            // TODO: actually implement this

            Scope.Global.Log.LogError(new LogEntry(
                "operators not yet implemented",
                "custom unary operator resolution has not been implemented yet. Sorry. :/",
                Location));

            return VoidExpression.Instance;
        }

        /// <summary>
        /// Creates a converter that analyzes unary operator nodes.
        /// </summary>
        public static Func<LNode, LocalScope, NodeConverter, IExpression> CreateUnaryOpConverter(
            Operator Op)
        {
            return (node, scope, conv) =>
            {
                if (!NodeHelpers.CheckArity(node, 1, scope.Log))
                    return VoidExpression.Instance;

                return CreateUnary(
                    Op,
                    conv.ConvertExpression(node.Args[0], scope),
                    scope.Function,
                    NodeHelpers.ToSourceLocation(node.Args[0].Range));
            };
        }

        /// <summary>
        /// Creates a converter that analyzes operator nodes which may
        /// either be unary or binary.
        /// </summary>
        public static Func<LNode, LocalScope, NodeConverter, IExpression> CreateUnaryOrBinaryOpConverter(
            Operator Op)
        {
            return (node, scope, conv) =>
            {
                if (!NodeHelpers.CheckMinArity(node, 1, scope.Log)
                    || !NodeHelpers.CheckMaxArity(node, 2, scope.Log))
                    return VoidExpression.Instance;

                if (node.ArgCount == 1)
                {
                    return CreateUnary(
                        Op,
                        conv.ConvertExpression(node.Args[0], scope),
                        scope.Function,
                        NodeHelpers.ToSourceLocation(node.Args[0].Range));
                }
                else
                {
                    return ExpressionConverters.CreateBinary(
                        Op,
                        conv.ConvertValue(node.Args[0], scope),
                        conv.ConvertValue(node.Args[1], scope),
                        scope.Function,
                        NodeHelpers.ToSourceLocation(node.Args[0].Range),
                        NodeHelpers.ToSourceLocation(node.Args[1].Range));
                }
            };
        }

        #endregion
    }
}

