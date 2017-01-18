using System;
using System.Collections.Generic;
using Loyc.Syntax;
using Flame.Compiler;
using LeMP;
using Loyc;

namespace Flame.Ecs.Parsing
{
    /// <summary>
    /// Represents a parsed source code file.
    /// </summary>
    public struct ParsedDocument
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Flame.Ecs.Parsing.ParsedDocument"/> class.
        /// </summary>
        /// <param name="Document">The document whose contents have been parsed.</param>
        /// <param name="Contents">The parsed contents of the document.</param>
        public ParsedDocument(
            ISourceDocument Document,
            IReadOnlyList<LNode> Contents)
        {
            this = default(ParsedDocument);
            this.Document = Document;
            this.Contents = Contents;
        }

        /// <summary>
        /// Gets the document whose contents have been parsed.
        /// </summary>
        /// <value>The document.</value>
        public ISourceDocument Document { get; private set; }

        /// <summary>
        /// Gets the parsed contents of the source document.
        /// </summary>
        /// <value>The contents.</value>
        public IReadOnlyList<LNode> Contents { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this instance is empty, i.e., 
        /// both the source document and the contents are null.
        /// </summary>
        /// <value><c>true</c> if this instance is empty; otherwise, <c>false</c>.</value>
        public bool IsEmpty { get { return Document == null && Contents == null; } }

        /// <summary>
        /// Gets the original source code of the parsed document, 
        /// without macro expansion.
        /// </summary>
        /// <value>The original source code.</value>
        public string OriginalSource { get { return Document.Source; } }

        /// <summary>
        /// Generate source code for the document's (macro-expanded) contents. 
        /// </summary>
        /// <returns>The macro-expanded source code.</returns>
        /// <param name="Sink">The message sink to log diagnostics to.</param>
        /// <param name="Printer">The node printer that is used to generate source code.</param>
        /// <param name="Options">The node printer options.</param>
        public string GetExpandedSource(
            ILNodePrinter Printer, IMessageSink Sink,
            ILNodePrinterOptions Options = null)
        {
            return Printer.Print(Contents, Sink, options: Options);
        }

        /// <summary>
        /// Expands all macros in this parsed document.
        /// </summary>
        /// <returns>A parsed document whose macros have been expanded.</returns>
        /// <param name="Processor">The macro processor to use.</param>
        public ParsedDocument ExpandMacros(MacroProcessor Processor)
        {
            return SourceHelpers.ExpandMacros(this, Processor);
        }

        /// <summary>
        /// Expands all macros in this parsed document.
        /// </summary>
        /// <returns>A parsed document whose macros have been expanded.</returns>
        /// <param name="Processor">The macro processor to use.</param>
        /// <param name="Prologue"></param>
        public ParsedDocument ExpandMacros(
            MacroProcessor Processor, IEnumerable<LNode> Prologue)
        {
            return SourceHelpers.ExpandMacros(this, Processor, Prologue);
        }

        /// <summary>
        /// The empty parsed document, with a null document and null contents.
        /// </summary>
        public static readonly ParsedDocument Empty = default(ParsedDocument);
    }
}

