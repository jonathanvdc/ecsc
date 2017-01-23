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
            LNode Node, GlobalScope Scope, NodeConverter Converter)
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
                                "could not resolve attribute type '", Node.ToString(), "'."),
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
                            "between classes '", Scope.TypeNamer.Convert(simpleType), 
                            "' and '", Scope.TypeNamer.Convert(suffixedType), "'."),
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
        /// <param name="Scope">The global scope.</param>
        /// <param name="Converter">The node converter.</param>
        public static IAttribute ConvertCustomAttribute(
            LNode Node, GlobalScope Scope, NodeConverter Converter)
        {
            var typeNode = Node.IsCall ? Node.Target : Node;
            var argNodes = Node.Args;

            var attrType = ConvertCustomAttributeType(typeNode, Scope, Converter);
            if (attrType == null)
                // Early-out here. We don't even know what type of
                // attribute we're constructing.
                return null;

            var localScope = Scope.CreateLocalScope();
            var ctorArgs = OverloadResolution.ConvertArguments(argNodes, localScope, Converter);

            var result = OverloadResolution.CreateCheckedNewObject(
                localScope.Function.GetInstanceConstructors(attrType),
                ctorArgs, localScope.Function, NodeHelpers.ToSourceLocation(Node.Range));

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
                            return null;
                        }
                        boundArgs.Add(bArg);
                    }
                    return new ConstructedAttribute(invExpr.Constructor, boundArgs);
                }
            }

            // Something went wrong. Return null.
            return null;
        }
    }
}

