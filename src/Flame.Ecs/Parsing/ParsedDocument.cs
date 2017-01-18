using System;
using System.Collections.Generic;
using Loyc.Syntax;
using Flame.Compiler;

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
    }
}

