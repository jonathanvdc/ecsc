using System;
using System.Linq;
using Loyc.Syntax;
using Flame.Compiler;
using Loyc.Collections;
using Flame.Compiler.Expressions;
using Flame.Attributes;
using System.Collections.Generic;

namespace Flame.Ecs
{
    public static class AttributeConverters
    {
        private const string AttributeSuffix = "Attribute";

        private static LNode AppendAttributeSuffix(LNode Node)
        {
            if (Node.IsId)
                return LNode.Id(Node.Name + AttributeSuffix, Node);
            else if (Node.Calls(CodeSymbols.Dot, 2))
                return LNode.Call(
                    CodeSymbols.Dot, 
                    new VList<LNode>() 
                    { 
                        Node.Args[0], 
                        AppendAttributeSuffix(Node.Args[1]) 
                    }, 
                    Node);
            else
                return Node;
        }

        private static bool IsAttributeClass(IType Type)
        {
            return Type != null
            && Type.GetIsReferenceType()
            && !Type.GetIsDelegate()
            && !Type.GetIsArray()
            && !Type.GetIsInterface();
        }

        /// <summary>
        /// Converts a type node for a custom attribute. This
        /// includes appending an 'Attribute' suffix if initial
        /// lookup fails.
        /// </summary>
        /// <returns>The custom attribute type.</returns>
        /// <param name="Node">The custom attribute type node.</param>
        /// <param name="Scope">The global scope.</param>
        /// <param name="Converter">The node converter.</param>
        public static IType ConvertCustomAttributeType(
            LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            var simpleType = Converter.ConvertType(Node, Scope);
            var suffixedType = Converter.ConvertType(
                AppendAttributeSuffix(Node), Scope);

            if (!IsAttributeClass(simpleType))
            {
                if (!IsAttributeClass(suffixedType))
                {
                    Scope.Log.LogError(new LogEntry(
                            "type resolution",
                            NodeHelpers.HighlightEven(
                                "cannot resolve attribute type '",
                                NodeHelpers.PrintTypeNode(Node),
                                "'."),
                            NodeHelpers.ToSourceLocation(Node.Range)));
                    return null;
                }

                return suffixedType;
            }
            else
            {
                if (IsAttributeClass(suffixedType))
                {
                    Scope.Log.LogError(new LogEntry(
                        "type resolution",
                        NodeHelpers.HighlightEven(
                            "attribute type '", Node.ToString(), "' is ambiguous " +
                            "between classes '", Scope.Function.Global.Renderer.Name(simpleType), 
                            "' and '", Scope.Function.Global.Renderer.Name(suffixedType), "'."),
                        NodeHelpers.ToSourceLocation(Node.Range)));
                }

                return simpleType;
            }
        }

        /// <summary>
        /// Converts the given custom attribute node.
        /// </summary>
        /// <returns>The analyzed custom attribute.</returns>
        /// <param name="Node">The custom attribute node.</param>
        /// <param name="Scope">The scope in which the attribute is analyzed.</param>
        /// <param name="Converter">The node converter.</param>
        public static IEnumerable<IAttribute> ConvertCustomAttribute(
            LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            var typeNode = Node.IsCall ? Node.Target : Node;
            var argNodes = Node.Args;

            var attrType = ConvertCustomAttributeType(typeNode, Scope, Converter);
            if (attrType == null)
                // Early-out here. We don't even know what type of
                // attribute we're constructing.
                return Enumerable.Empty<IAttribute>();

            var ctorArgs = OverloadResolution.ConvertArguments(argNodes, Scope, Converter);

            var result = OverloadResolution.CreateCheckedNewObject(
                Scope.Function.GetInstanceConstructors(attrType),
                ctorArgs, Scope.Function, NodeHelpers.ToSourceLocation(Node.Range));

            // HACK: extract the method and arguments from the 
            //       new-object expression. This should be revisited
            //       once method overload resolution has been implemented
            //       properly.
            var essentialResult = result.GetEssentialExpression();
            if (essentialResult is NewObjectExpression)
            {
                var invExpr = (NewObjectExpression)essentialResult;
                if (invExpr.Constructor != null)
                {
                    var boundArgs = new List<IBoundObject>();
                    foreach (var argPair in invExpr.Arguments.Zip(ctorArgs))
                    {
                        var bArg = argPair.A.EvaluateOrNull();
                        if (bArg == null)
                        {
                            Scope.Log.LogError(new LogEntry(
                                "cannot evaluate",
                                NodeHelpers.HighlightEven(
                                    "argument is not a primitive value or " +
                                    "cannot be evaluated at compile-time."),
                                argPair.B.Item2));
                            return Enumerable.Empty<IAttribute>();
                        }
                        boundArgs.Add(bArg);
                    }
                    return new IAttribute[] { new ConstructedAttribute(invExpr.Constructor, boundArgs) };
                }
            }

            // Something went wrong. Return nothing.
            return Enumerable.Empty<IAttribute>();
        }

        /// <summary>
        /// Converts the given intrinsic attribute node (type #builtin_attribute).
        /// </summary>
        /// <param name="Node">The intrinsic attribute node to convert.</param>
        /// <param name="Scope">The scope in which the attribute is analyzed.</param>
        /// <param name="Converter">The node converter.</param>
        /// <returns>A sequence of attributes.</returns>
        public static IEnumerable<IAttribute> ConvertBuiltinAttribute(
            LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckMinArity(Node, 1, Scope.Log)
                || !NodeHelpers.CheckId(Node.Args[0], Scope.Log))
            {
                return Enumerable.Empty<IAttribute>();
            }

            var attributeName = Node.Args[0].Name.Name;

            var args = new List<IBoundObject>(Node.Args.Count - 1);
            foreach (var argNode in Node.Args.Skip(1))
            {
                var expr = Converter.ConvertExpression(argNode, Scope);
                var exprObj = expr.EvaluateOrNull();
                if (exprObj == null)
                {
                    Scope.Log.LogError(new LogEntry(
                        "cannot evaluate",
                        NodeHelpers.HighlightEven(
                            "argument is not a primitive value or " +
                            "cannot be evaluated at compile-time."),
                        NodeHelpers.ToSourceLocation(argNode.Range)));
                    return Enumerable.Empty<IAttribute>();
                }

                args.Add(exprObj);
            }

            return new IAttribute[] { new IntrinsicAttribute(attributeName, args) };
        }
    }
}

