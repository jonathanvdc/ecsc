using System;
using Flame.Compiler;
using Flame.Build;

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
		{
			this.Binder = Binder;
			this.ConversionRules = ConversionRules;
			this.Log = Log;
			this.TypeNamer = TypeNamer;
		}

		public IBinder Binder { get; private set; }
		public IConversionRules ConversionRules { get; private set; }
		public ICompilerLog Log { get; private set; }
		public TypeConverterBase<string> TypeNamer { get; private set; }
	}
}

