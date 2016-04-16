using System;
using Flame.Compiler;
using Flame.Build;
using Flame.Compiler.Expressions;
using Pixie;

namespace Flame.Ecs
{
	/// <summary>
	/// A global scope: a scope that is not associated 
	/// with any particular function.
	/// </summary>
	public class GlobalScope
	{
		public GlobalScope(
			IBinder Binder, IConversionRules ConversionRules, 
			ICompilerLog Log, TypeConverterBase<string> TypeNamer)
			: this(new QualifiedBinder(Binder), ConversionRules, Log, TypeNamer)
		{ }
		public GlobalScope(
			QualifiedBinder Binder, IConversionRules ConversionRules, 
			ICompilerLog Log, TypeConverterBase<string> TypeNamer)
		{
			this.Binder = Binder;
			this.ConversionRules = ConversionRules;
			this.Log = Log;
			this.TypeNamer = TypeNamer;
		}

		public QualifiedBinder Binder { get; private set; }
		public IConversionRules ConversionRules { get; private set; }
		public ICompilerLog Log { get; private set; }
		public TypeConverterBase<string> TypeNamer { get; private set; }

		public IEnvironment Environment { get { return Binder.Binder.Environment; } }

		public GlobalScope WithBinder(QualifiedBinder NewBinder)
		{
			return new GlobalScope(NewBinder, ConversionRules, Log, TypeNamer);
		}

		private static MarkupNode[] HighlightEven(params string[] Text)
		{
			var results = new MarkupNode[Text.Length];
			for (int i = 0; i < Text.Length; i++)
			{
				results[i] = new MarkupNode(
					i % 2 == 0 ? NodeConstants.TextNodeType : NodeConstants.BrightNodeType,
					Text[i]);
			}
			return results;
		}

		/// <summary>
		/// Implicitly converts the given expression to the given type.
		/// A diagnostic is issued if this is not a legal operation,
		/// but the resulting expression is always of the given target type,
		/// and is never null.
		/// </summary>
		public IExpression ConvertImplicit(IExpression From, IType To, SourceLocation Location)
		{
			var result = ConversionRules.TryConvertImplicit(From, To);
			if (result != null)
			{
				return result;
			}
			else
			{
				result = ConversionRules.TryConvertExplicit(From, To);
				Log.LogError(new LogEntry(
					"no implicit conversion", 
					HighlightEven(
						"cannot implicitly convert type '", TypeNamer.Convert(From.Type), "' to '", TypeNamer.Convert(To), "'." + 
						(result != null ? " An explicit conversion exists. (are you missing a cast?)" : "")),
					Location));
				
				if (result == null)
					return new UnknownExpression(To);
				else
					return result;
			}
		}

		/// <summary>
		/// Explicitly converts the given expression to the given type.
		/// A diagnostic is issued if this is not a legal operation,
		/// but the resulting expression is always of the given target type,
		/// and is never null.
		/// </summary>
		public IExpression ConvertExplicit(IExpression From, IType To, SourceLocation Location)
		{
			var result = ConversionRules.TryConvertExplicit(From, To);
			if (result != null)
			{
				return result;
			}
			else
			{
				Log.LogError(new LogEntry(
					"no conversion", 
					HighlightEven("cannot convert type '", TypeNamer.Convert(From.Type), "' to '", TypeNamer.Convert(To), "'."),
					Location));
				
				return new UnknownExpression(To);
			}
		}
	}
}

