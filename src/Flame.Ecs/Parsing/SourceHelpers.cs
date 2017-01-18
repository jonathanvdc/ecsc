using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Flame.Compiler;
using Loyc.Syntax;
using Loyc;
using LeMP;
using Loyc.Collections;

namespace Flame.Ecs.Parsing
{
    /// <summary>
    /// Contains functionality that helps with parsing 
    /// and macro-expanding source code.
    /// </summary>
    public static class SourceHelpers
    {
        /// <summary>
        /// Uses the given parsing service to parse a source document.
        /// Diagnostics are logged to a sink.
        /// </summary>
        /// <returns>The parsed source document.</returns>
        /// <param name="Source">The source document to parse.</param>
        /// <param name="Service">
        /// The parsing service with which the document is to be parsed.
        /// </param>
        /// <param name="Sink">The message sink to log diagnostics to.</param>
        public static ParsedDocument Parse(
            ISourceDocument Source, IParsingService Service,
            IMessageSink Sink)
        {
            var lexer = Service.Tokenize(
                new UString(Source.Source), Source.Identifier, Sink);

            return new ParsedDocument(Source, Service.Parse(lexer, Sink));
        }

        /// <summary>
        /// Registers the given source document with the sink.
        /// Then uses the given parsing service to parse a source document.
        /// Diagnostics are logged to a sink.
        /// </summary>
        /// <returns>The parsed source document.</returns>
        /// <param name="Source">The source document to parse.</param>
        /// <param name="Service">
        /// The parsing service with which the document is to be parsed.
        /// </param>
        /// <param name="Sink">
        /// The message sink to log diagnostics to.
        /// The source document is registered with this sink.
        /// </param>
        public static ParsedDocument RegisterAndParse(
            ISourceDocument Source, IParsingService Service,
            CompilerLogMessageSink Sink)
        {
            Sink.DocumentCache.Add(Source);

            return Parse(Source, Service, Sink);
        }

        /// <summary>
        /// Expands all macros in the given sequence of nodes.
        /// </summary>
        /// <returns>The macro-expanded sequence of nodes.</returns>
        /// <param name="Nodes">The nodes to macro-expand.</param>
        /// <param name="Processor">The macro processor.</param>
        public static IReadOnlyList<LNode> ExpandMacros(
            IEnumerable<LNode> Nodes, MacroProcessor Processor)
        {
            return Processor.ProcessSynchronously(new VList<LNode>(Nodes));
        }

        /// <summary>
        /// Prepends a prologue to a sequence of nodes, then 
        /// expands all macros in the resulting node sequence.
        /// </summary>
        /// <returns>The macro-expanded sequence of nodes.</returns>
        /// <param name="Nodes">The nodes to macro-expand.</param>
        /// <param name="Prologue">The prologue to prepend to the node sequence.</param>
        /// <param name="Processor">The macro processor.</param>
        public static IReadOnlyList<LNode> ExpandMacros(
            IEnumerable<LNode> Nodes, MacroProcessor Processor,
            IEnumerable<LNode> Prologue)
        {
            return ExpandMacros(Prologue.Concat(Nodes), Processor);
        }

        /// <summary>
        /// Expands all macros in a parsed document.
        /// </summary>
        /// <returns>The macro-expanded parsed document.</returns>
        /// <param name="Nodes">The document to macro-expand.</param>
        /// <param name="Processor">The macro processor.</param>
        public static ParsedDocument ExpandMacros(
            ParsedDocument Source, MacroProcessor Processor)
        {
            return new ParsedDocument(
                Source.Document, 
                ExpandMacros(Source.Contents, Processor));
        }

        /// <summary>
        /// Prepends a prologue to a document, then 
        /// expands all macros in the resulting document.
        /// </summary>
        /// <returns>The macro-expanded parsed document.</returns>
        /// <param name="Nodes">The document to macro-expand.</param>
        /// <param name="Prologue">The prologue to prepend to the node sequence.</param>
        /// <param name="Processor">The macro processor.</param>
        public static ParsedDocument ExpandMacros(
            ParsedDocument Source, MacroProcessor Processor,
            IEnumerable<LNode> Prologue)
        {
            return new ParsedDocument(
                Source.Document, 
                ExpandMacros(Source.Contents, Processor, Prologue));
        }
    }
}

