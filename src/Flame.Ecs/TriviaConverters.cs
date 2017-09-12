using System;
using Loyc.Syntax;
using Flame.Compiler;
using EcscMacros;
using System.Collections.Generic;
using System.Linq;

namespace Flame.Ecs
{
    /// <summary>
    /// A collection of helpers that handle the parsing of trivia attributes.
    /// </summary>
    public static class TriviaConverters
    {
        /// <summary>
        /// Parses the given documentation comment node as an attribute.
        /// </summary>
        /// <returns>The documentation comment, as an attribute.</returns>
        /// <param name="Node">The documentation comment trivia node.</param>
        /// <param name="Scope">The global scope.</param>
        /// <param name="Converter">The node converter.</param>
        public static IEnumerable<IAttribute> ConvertDocumentationComment(
            LNode Node, LocalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckArity(Node, 1, Scope.Log))
                // Early-out here. Documentation comment nodes should contain
                // exactly one string literal.
                return Enumerable.Empty<IAttribute>();

            var docLiteral = Node.Args[0];
            string doc = ParseDocumentationCommentLiteral(docLiteral, Scope);
            if (doc == null)
            {
                return Enumerable.Empty<IAttribute>();
            }
            else
            {
                return Scope.Function.Global.DocumentationParser.Parse(
                    doc,
                    NodeHelpers.ToSourceLocation(docLiteral.Range),
                    Scope.Log);
            }
        }

        private static string ParseDocumentationCommentLiteral(
            LNode Node, LocalScope Scope)
        {
            if (Node.Calls(CodeSymbols.Add))
            {
                var childLiterals = Node.Args
                    .Select(n => ParseDocumentationCommentLiteral(n, Scope))
                    .ToArray();

                if (childLiterals.Contains(null))
                {
                    return null;
                }
                else
                {
                    return string.Concat(childLiterals);
                }
            }
            else if (Node.IsLiteral)
            {
                return Node.Value.ToString();
            }
            else
            {
                Scope.Log.LogWarning(
                    new LogEntry(
                        "syntax error",
                        NodeHelpers.HighlightEven(
                            "expected literal in documentation comment ('",
                            EcscSymbols.TriviaDocumentationComment.Name,
                            "') node."),
                        NodeHelpers.ToSourceLocation(Node.Range)));
                return null;
            }
        }
    }
}

