using System;
using Loyc.Syntax;
using Flame.Compiler;
using EcscMacros;
using System.Collections.Generic;

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
            LNode Node, GlobalScope Scope, NodeConverter Converter)
        {
            if (!NodeHelpers.CheckArity(Node, 1, Scope.Log))
                // Early-out here. Documentation comment nodes should contain
                // exactly one string literal.
                return null;

            var docLiteral = Node.Args[0];
            if (!docLiteral.IsLiteral)
            {
                Scope.Log.LogWarning(
                    new LogEntry(
                        "syntax error",
                        NodeHelpers.HighlightEven(
                            "expected literal in documentation comment ('",
                            EcscSymbols.TriviaDocumentationComment.Name,
                            "') node."),
                        NodeHelpers.ToSourceLocation(docLiteral.Range)));
                return null;
            }

            string doc = docLiteral.Value.ToString();
            return Scope.DocumentationParser.Parse(
                doc,
                NodeHelpers.ToSourceLocation(docLiteral.Range),
                Scope.Log);
        }
    }
}

