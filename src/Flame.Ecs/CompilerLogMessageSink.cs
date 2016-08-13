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
    /// A Loyc message sink that pipes its output to a Flame compiler log.
    /// </summary>
    public sealed class CompilerLogMessageSink : IMessageSink
    {
        public CompilerLogMessageSink(ICompilerLog Log)
        {
            this.Log = Log;
        }

        /// <summary>
        /// Gets the message sink's inner compiler log.
        /// </summary>
        public ICompilerLog Log { get; private set; }

        public static MarkupNode GetContextMarkupNode(object Context)
        {
            if (Context is SourceRange)
                return NodeHelpers.ToSourceLocation((SourceRange)Context).CreateDiagnosticsNode();
            else if (Context is LNode)
                return NodeHelpers.ToSourceLocation(((LNode)Context).Range).CreateDiagnosticsNode();
            else if (Context is IHasLocation)
                return GetContextMarkupNode(((IHasLocation)Context).Location);
            else if (Context == null)
                return new MarkupNode(NodeConstants.TextNodeType, "");
            else
                return new MarkupNode(NodeConstants.RemarksNodeType, Context.ToString());
        }

        public static LogEntry ToLogEntry(string Title, string Message, object Context)
        {
            return new LogEntry(Title, new MarkupNode[]
            {
                new MarkupNode(NodeConstants.TextNodeType, Message),
                GetContextMarkupNode(Context)
            }); 
        }

        public static LogEntry ToLogEntry(
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