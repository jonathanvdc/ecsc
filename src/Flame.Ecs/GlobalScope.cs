using System;
using System.Collections.Generic;
using System.Threading;
using Flame.Compiler;
using Flame.Build;
using Flame.Compiler.Expressions;
using Pixie;
using Flame.Ecs.Semantics;
using Loyc;

namespace Flame.Ecs
{
    /// <summary>
    /// A global scope: a scope that is not associated 
    /// with any particular function.
    /// </summary>
    public sealed class GlobalScope
    {
        public GlobalScope(
            IBinder Binder, ConversionRules ConversionRules, 
            ICompilerLog Log, TypeConverterBase<string> TypeNamer)
            : this(new QualifiedBinder(Binder), ConversionRules, Log, TypeNamer)
        {
        }

        public GlobalScope(
            QualifiedBinder Binder, ConversionRules ConversionRules, 
            ICompilerLog Log, TypeConverterBase<string> TypeNamer)
            : this(
                Binder, ConversionRules, Log, TypeNamer, 
                new ThreadLocal<GlobalMemberCache>(() => new GlobalMemberCache()))
        {
        }

        private GlobalScope(
            QualifiedBinder Binder, ConversionRules ConversionRules, 
            ICompilerLog Log, TypeConverterBase<string> TypeNamer,
            ThreadLocal<GlobalMemberCache> MemberCache)
        {
            this.Binder = Binder;
            this.ConversionRules = ConversionRules;
            this.Log = Log;
            this.TypeNamer = TypeNamer;
            this.memCache = MemberCache;
            this.extMemCache = new ThreadLocal<ExtensionMemberCache>(
                CreateExtensionMemberCache);
        }

        /// <summary>
        /// Gets the qualified binder for this global scope.
        /// </summary>
        /// <value>The binder.</value>
        public QualifiedBinder Binder { get; private set; }

        /// <summary>
        /// Gets the set of conversion rules for this
        /// global scope.
        /// </summary>
        /// <value>The conversion rules.</value>
        public ConversionRules ConversionRules { get; private set; }

        /// <summary>
        /// Gets the compiler log for this global scope.
        /// </summary>
        /// <value>The log.</value>
        public ICompilerLog Log { get; private set; }

        /// <summary>
        /// Gets the type namer for this global scope.
        /// </summary>
        /// <value>The type namer.</value>
        public TypeConverterBase<string> TypeNamer { get; private set; }

        private ThreadLocal<GlobalMemberCache> memCache;
        private ThreadLocal<ExtensionMemberCache> extMemCache;

        /// <summary>
        /// Gets the global member cache for this global scope.
        /// </summary>
        /// <value>The member cache.</value>
        public GlobalMemberCache MemberCache
        { 
            get { return memCache.Value; }
        }

        /// <summary>
        /// Gets the extension member cache for this global scope.
        /// </summary>
        /// <value>The member cache.</value>
        public ExtensionMemberCache ExtensionMemberCache
        {
            get { return extMemCache.Value; }
        }

        /// <summary>
        /// Gets the environment for this global scope.
        /// </summary>
        /// <value>The environment.</value>
        public IEnvironment Environment { get { return Binder.Binder.Environment; } }

        /// <summary>
        /// Creates a local scope that is enclosed by
        /// this global scope. The resulting local scope
        /// has no enclosing function, enclosing type
        /// or parameter list.
        /// </summary>
        /// <returns>A new local scope.</returns>
        public LocalScope CreateLocalScope()
        {
            return new LocalScope(new FunctionScope(
                    this, null, null, null, 
                    new Dictionary<Symbol, IVariable>()));
        }

        public GlobalScope WithBinder(QualifiedBinder NewBinder)
        {
            return new GlobalScope(
                NewBinder, ConversionRules, Log, TypeNamer, memCache);
        }

        private ExtensionMemberCache CreateExtensionMemberCache()
        {
            return new ExtensionMemberCache(Binder, memCache.Value);
        }
    }
}

