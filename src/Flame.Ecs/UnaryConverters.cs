using System;
using System.Collections.Generic;
using System.Linq;
using Flame.Compiler;
using Flame.Compiler.Expressions;
using Flame.Compiler.Statements;
using Flame.Compiler.Variables;
using Flame.Ecs.Semantics;
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
            string OperatorName, Func<IVariable, IType, IExpression> CreatePrimitiveExpression)
        {
            return (node, scope, converter) =>
            {
                if (!NodeHelpers.CheckArity(node, 1, scope.Log))
                    return VoidExpression.Instance;

                var innerExpr = converter.ConvertExpression(node.Args[0], scope);
                var innerVar = ExpressionConverters.AsVariable(innerExpr);

                if (innerVar == null)
                {
                    scope.Log.LogError(new LogEntry(
                        "syntax error",
                        "the operand of an increment or decrement operator must be a variable, property or indexer.",
                        NodeHelpers.ToSourceLocation(node.Range)));
                }

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
                        new IntegerExpression(-Increment), Type).Optimize());
            }
            else
            {
                return new AddExpression(
                    Variable.CreateGetExpression(), 
                    new StaticCastExpression(
                        new IntegerExpression(Increment), Type).Optimize());
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

        #endregion

        #region Indexing

        /// <summary>
        /// Converts an indexing expression (type @_[]).
        /// </summary>
        public static IExpression ConvertIndex(
            LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckMinArity(Node, 2, Scope.Log))
                return VoidExpression.Instance;

            var containerExpr = Converter.ConvertExpression(Node.Args[0], Scope);
            var dimExprs = OverloadResolution.ConvertArguments(Node.Args.Slice(1), Scope, Converter);

            var containerTy = containerExpr.Type;

            if (containerTy.GetIsArray() || containerTy.GetIsVector())
            {
                // We will handle built-in container types (arrays, vectors)
                // here, because they are a special case.

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
                            "[]", ", expected '", dims.ToString(), "'.")));
                    return new UnknownExpression(elemTy);
                }

                foreach (var item in dimExprs)
                {
                    var ty = item.Item1.Type;
                    if (!ty.GetIsInteger() && !PrimitiveTypes.Char.Equals(ty))
                    {
                        Scope.Log.LogError(new LogEntry(
                            "type error",
                            NodeHelpers.HighlightEven(
                                "index inside ", "[]", " of type '", Scope.Function.Global.TypeNamer.Convert(ty),
                                "' was not an integer.")));
                    }
                }
                return new ElementVariable(containerExpr, dimExprs.Select(t => t.Item1)).CreateGetExpression();
            }
            else
            {
                var indexers = 
                    Scope.Function.GetInstanceIndexers(containerTy)
                        .Select(p => new IndexerDelegateExpression(p, containerExpr))
                        .ToArray();

                var srcLoc = NodeHelpers.ToSourceLocation(Node.Range);
                if (indexers.Length == 0)
                {
                    Scope.Log.LogError(new LogEntry(
                        "type error", 
                        NodeHelpers.HighlightEven(
                            "cannot apply indexing with '", "[]", 
                            "' to an expression of type '", 
                            Scope.Function.Global.TypeNamer.Convert(containerTy), "'."),
                        srcLoc));
                    return VoidExpression.Instance;
                }

                return OverloadResolution.CreateCheckedInvocation(
                    "indexer", indexers, dimExprs, 
                    Scope.Function.Global, srcLoc);
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
            GlobalScope Scope,
            SourceLocation Location)
        {
            var ty = Operand.Type;

            IType opTy;
            if (UnaryOperatorResolution.TryGetPrimitiveOperatorType(Op, ty, out opTy))
            {
                if (opTy == null)
                {
                    Scope.Log.LogError(new LogEntry(
                        "operator application",
                        NodeHelpers.HighlightEven(
                            "operator '", Op.Name, "' cannot be applied to an operand of type '", 
                            Scope.TypeNamer.Convert(ty), "'."),
                        Location));
                    return new UnknownExpression(ty);
                }

                return CreatePrimitiveUnary(
                    Op, Scope.ConvertImplicit(Operand, opTy, Location));
            }

            // TODO: actually implement this

            Scope.Log.LogError(new LogEntry(
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
                    scope.Function.Global,
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
                        scope.Function.Global,
                        NodeHelpers.ToSourceLocation(node.Args[0].Range));
                }
                else
                {
                    return ExpressionConverters.CreateBinary(
                        Op, 
                        conv.ConvertExpression(node.Args[0], scope), 
                        conv.ConvertExpression(node.Args[1], scope), 
                        scope.Function,
                        NodeHelpers.ToSourceLocation(node.Args[0].Range),
                        NodeHelpers.ToSourceLocation(node.Args[1].Range));
                }
            };
        }

        #endregion
    }
}

