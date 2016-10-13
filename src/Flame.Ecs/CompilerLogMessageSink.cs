using Loyc;
using Loyc.Syntax;
using Flame;
using Flame.Compiler;
using Pixie;
using System;
using System.Collections.Generic;

namespace Flame.Ecs
{
    /// <summary>
    /// Describes a set of source documents, which can be 
    /// addressed by (file)name.
    /// </summary>
    public sealed class SourceDocumentCache
    {
        public SourceDocumentCache()
        {
            this.docs = new Dictionary<string, ISourceDocument>();
        }

        private Dictionary<string, ISourceDocument> docs;

        /// <summary>
        /// Gets the <see cref="Flame.Ecs.SourceDocumentCache"/> with the specified name.
        /// </summary>
        /// <param name="Name">The name of the document to retrieve.</param>
        public ISourceDocument this[string Name]
        {
            get { return docs[Name]; }
        }

        /// <summary>
        /// Tries to get the <see cref="Flame.Ecs.SourceDocumentCache"/> with the specified name.
        /// </summary>
        /// <returns><c>true</c>, if get document was successfully retrieved, <c>false</c> otherwise.</returns>
        /// <param name="Name">The name of the document to retrieve.</param>
        /// <param name="Result">The location to store the resulting source document in.</param>
        public bool TryGetDocument(string Name, out ISourceDocument Result)
        {
            return docs.TryGetValue(Name, out Result);
        }

        /// <summary>
        /// Add the specified document to this source document cache.
        /// </summary>
        public void Add(ISourceDocument Document)
        {
            docs[Document.Identifier] = Document;
        }
    }

    /// <summary>
    /// A Loyc message sink that pipes its output to a Flame compiler log.
    /// </summary>
    public sealed class CompilerLogMessageSink : IMessageSink
    {
        public CompilerLogMessageSink(
            ICompilerLog Log, SourceDocumentCache DocumentCache)
        {
            this.Log = Log;
            this.DocumentCache = DocumentCache;
        }

        /// <summary>
        /// Gets the message sink's inner compiler log.
        /// </summary>
        public ICompilerLog Log { get; private set; }

        /// <summary>
        /// Gets the source document cache.
        /// </summary>
        /// <value>The source document cache.</value>
        public SourceDocumentCache DocumentCache { get; private set; }

        public MarkupNode GetContextMarkupNode(object Context)
        {
            if (Context is SourceRange)
                return NodeHelpers.ToSourceLocation((SourceRange)Context).CreateDiagnosticsNode();
            else if (Context is LNode)
                return NodeHelpers.ToSourceLocation(((LNode)Context).Range).CreateDiagnosticsNode();
            else if (Context is IHasLocation)
                return GetContextMarkupNode(((IHasLocation)Context).Location);
            else if (Context is SourcePos)
                return GetContextMarkupNode((SourcePos)Context);
            else if (Context == null)
                return new MarkupNode(NodeConstants.TextNodeType, "");
            else
                return new MarkupNode(NodeConstants.RemarksNodeType, Context.ToString());
        }

        public MarkupNode GetContextMarkupNode(SourcePos Context)
        {
            ISourceDocument doc;
            if (DocumentCache.TryGetDocument(Context.FileName, out doc)
                && Context.Line <= doc.LineCount)
            {
                int pos = 0;
                for (int i = 0; i < Context.Line - 1; i++)
                {
                    // Increment the position by the line's length, plus its
                    // trailing newline character.
                    pos += doc.GetLine(i).Length + 1;
                }
                pos += Context.PosInLine - 1;
                return new SourceLocation(doc, pos).CreateDiagnosticsNode();
            }
            else
            {
                return new MarkupNode(NodeConstants.RemarksNodeType, Context.ToString());
            }
        }

        public LogEntry ToLogEntry(string Title, string Message, object Context)
        {
            return new LogEntry(Title, new MarkupNode[]
            {
                new MarkupNode(NodeConstants.TextNodeType, Message),
                GetContextMarkupNode(Context)
            }); 
        }

        public LogEntry ToLogEntry(
            Func<string, Tuple<string, string>> TitleSplitter, string Message, object Context)
        {
            var splitTitle = TitleSplitter(Message);
            return ToLogEntry(splitTitle.Item1, splitTitle.Item2, Context);
        }

        private static readonly char[] punctuation = new char[]
		{ '.', ';', ',', '?', '!', '(', ')', '[', ']' };

        public static Tuple<string, string> SplitTitleByPunctuation(string FullMessage)
        {
            var colonIndex = FullMessage.IndexOf(':');
            if (colonIndex < FullMessage.IndexOfAny(punctuation))
            {
                return Tuple.Create(
                    FullMessage.Substring(0, colonIndex).TrimEnd(), 
                    FullMessage.Substring(colonIndex + 1).TrimStart());
            }
            else
            {
                return Tuple.Create("", FullMessage);
            }
        }

        public void WriteEntry(Severity Level, LogEntry Entry)
        {
            if (Level >= Severity.Error)
                Log.LogError(Entry);
            else if (Level >= Severity.Warning)
                Log.LogWarning(Entry);
            else if (Level >= Severity.Debug)
                Log.LogMessage(Entry);
            else
                Log.LogEvent(Entry);
        }

        public void Write(Severity Level, string Message, object Context)
        {
            WriteEntry(Level, ToLogEntry(SplitTitleByPunctuation, Message, Context));
        }

        public bool IsEnabled(Severity Level)
        {
            return true;
        }

        public void Write(Severity Level, object Context, string Format, params object[] Args)
        {
            Write(Level, Format.Localized(Args), Context);
        }

        public void Write(Severity Level, object Context, string Format)
        {
            Write(Level, Format.Localized(), Context);
        }

        public void Write(Severity Level, object Context, string Format, object Arg0, object Arg1 = null)
        {
            Write(Level, Format.Localized(Arg0, Arg1), Context);
        }
    }
}