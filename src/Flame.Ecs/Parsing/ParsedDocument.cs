using System;
using System.Collections.Generic;
using Loyc.Syntax;
using Flame.Compiler;
using LeMP;

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
    }
}

