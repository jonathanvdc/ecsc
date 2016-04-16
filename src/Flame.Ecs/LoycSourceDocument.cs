using System;
using Loyc;
using Loyc.Syntax;
using Flame;
using Flame.Compiler;

namespace Flame.Ecs
{
	/// <summary>
	/// A Flame source document wrapper for Loyc source files.
	/// </summary>
	public class LoycSourceDocument : ISourceDocument, IEquatable<LoycSourceDocument>
	{
		public LoycSourceDocument(ISourceFile SourceFile)
		{
			this.SourceFile = SourceFile;
		}

		public ISourceFile SourceFile { get; private set; }

		public string Identifier { get { return SourceFile.FileName; } }
		public int Length { get { return SourceFile.Text.Count; } }
		public string Source { get { return Slice(0, Length); } }

		public int LineCount 
		{
			get
			{
				var srcPos = SourceFile.IndexToLine(this.Length - 1);
				if (srcPos.Line > 0 && srcPos.PosInLine > 0)
					return srcPos.Line + 1;
				else
					return 0;
			}
		}

		private static int ClampIndex(int Max, int Alternative, int Index)
		{
			if (Index < 0 || Index > Max)
				return Alternative;
			else
				return Index;
		}

		public string Slice(int StartIndex, int Length)
		{
			var slice = SourceFile.Text.Slice(StartIndex, Length);
			return slice.ToString().TrimEnd();
		}

		public SourceGridPosition ToGridPosition(int Index)
		{
			var pos = SourceFile.IndexToLine(Index);
			return new SourceGridPosition(pos.Line - 1, pos.PosInLine - 1);
		}

		public string GetLine(int Index)
		{
			var thisLine = ClampIndex(Length, 0, SourceFile.LineToIndex(Index + 1));
			var lineCount = LineCount;
			var nextLine = Index + 2 >= lineCount 
				? Length
				: ClampIndex(Length, Length, SourceFile.LineToIndex(Index + 2));
			return Slice(thisLine, nextLine - thisLine);
		}

		public bool Equals(LoycSourceDocument Other)
		{
			return SourceFile == Other.SourceFile;
		}

		public override bool Equals(object obj)
		{
			return obj is LoycSourceDocument && Equals((LoycSourceDocument)obj);
		}

		public override int GetHashCode()
		{
			return SourceFile.GetHashCode();
		}

		public override string ToString()
		{
			return SourceFile.ToString();
		}
	}
}