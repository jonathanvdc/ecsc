using System;
using System.Text;
using LeMP;
using Loyc.Syntax;
using Loyc;
using System.Collections.Generic;
using Loyc.Collections;

namespace EcscMacros
{
    /// <summary>
    /// Defines functionality that preprocess documentation comments before feeding
    /// them to the compiler.
    /// </summary>
    [ContainsMacros]
    public class DocumentationMacros
    {
        public DocumentationMacros()
        {
        }

        /// <summary>
        /// Determines if the specified node is a single-line documentation comment.
        /// </summary>
        /// <returns><c>true</c> if the specified node is a single-line documentation comment; otherwise, <c>false</c>.</returns>
        /// <param name="Node">The node which may be a single-line documentation comment.</param>
        public static bool IsSinglelineDocumentationComment(LNode Node)
        {
            if (Node.Calls(CodeSymbols.TriviaSLComment, 1))
            {
                string commentDoc = Node.Args[0].Value.ToString();
                return commentDoc.Length > 0 && commentDoc[0] == '/';
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Determines if the specified node is a multi-line documentation comment.
        /// </summary>
        /// <returns><c>true</c> if the specified node is a multi-line documentation comment; otherwise, <c>false</c>.</returns>
        /// <param name="Node">The node which may be a multi-line documentation comment.</param>
        public static bool IsMultilineDocumentationComment(LNode Node)
        {
            if (Node.Calls(CodeSymbols.TriviaMLComment, 1))
            {
                string commentDoc = Node.Args[0].Value.ToString();
                return commentDoc.Length > 0 && commentDoc[0] == '*';
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Tests if the given node has any un-processed documentation comment attributes.
        /// </summary>
        /// <returns><c>true</c>, if the given node has any un-processed documentation comment attributes, <c>false</c> otherwise.</returns>
        /// <param name="Node">The node which is to be scanned for documentation comments.</param>
        public static bool ContainsDocumentationComments(LNode Node)
        {
            foreach (var item in Node.Attrs)
            {
                if (IsSinglelineDocumentationComment(item)
                    || IsMultilineDocumentationComment(item))
                {
                    return true;
                }
            }
            return false;
        }

        [LexicalMacro(
            "[#trivia_SL_comment(\"/<summary>\"), ...] #fn(...)",
            "converts 'special' comments to #trivia_doc_comment nodes.",
            "#fn", "#var", "#property", "#event", "#class", "#struct",
            "#enum", "#interface", "#delegate",
            Mode = MacroMode.Passive | MacroMode.Normal)]
        public static LNode ProcessDocumentationComments(LNode Node, IMacroContext Sink)
        {
            if (!ContainsDocumentationComments(Node))
                return null;

            var newAttrs = new VList<LNode>();
            var docs = new StringBuilder();
            foreach (var item in Node.Attrs)
            {
                if (IsSinglelineDocumentationComment(item))
                {
                    // Single-line documentation comments start with '///'.
                    // Since the '//' part is parsed as part of the comment, we only
                    // need to trim the leading '/'.
                    docs.AppendLine(item.Args[0].Value.ToString().Substring(1));
                }
                else if (IsMultilineDocumentationComment(item))
                {
                    // Multi-line documentation comments start with '/**'.
                    // Since the '/*' part is parsed as part of the comment, we only
                    // need to trim the leading '*'.
                    string docContents = item.Args[0].Value.ToString().Substring(1);
                    // However, there's a catch. Multi-line documentation comments
                    // may contain lines that start with an asterisk. These should
                    // be removed.
                    foreach (var line in docContents.Split(
                        new string[] { Environment.NewLine }, StringSplitOptions.None))
                    {
                        // Extract the parenthesized part from [\w]*[\*]*(.*)
                        docs.AppendLine(line.TrimStart(null).TrimStart('*'));
                    }
                }
                else
                {
                    newAttrs.Add(item);
                }
            }
            var docAttr = LNode.Call(
                EcscSymbols.TriviaDocumentationComment, 
                new VList<LNode>(LNode.Literal(docs.ToString(), Node.Range)),
                Node.Range);
            newAttrs.Add(docAttr);
            return Node.WithAttrs(newAttrs);
        }
    }
}

