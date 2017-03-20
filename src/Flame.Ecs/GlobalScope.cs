using System;
using System.Collections.Generic;
using System.Threading;
using Flame.Build;
using Flame.Compiler;
using Flame.Compiler.Expressions;
using Flame.Ecs.Semantics;
using Flame.Syntax;
using Pixie;
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
            ICompilerLog Log, TypeConverterBase<string> TypeNamer,
            IDocumentationParser DocumentationParser)
            : this(
                new QualifiedBinder(Binder), ConversionRules,
                Log, TypeNamer, DocumentationParser)
        {
        }

        public GlobalScope(
            QualifiedBinder Binder, ConversionRules ConversionRules,
            ICompilerLog Log, TypeConverterBase<string> TypeNamer,
            IDocumentationParser DocumentationParser)
            : this(
                Binder, ConversionRules, Log, TypeNamer, DocumentationParser,
                new ThreadLocal<GlobalMemberCache>(() => new GlobalMemberCache()),
                new WarningStack())
        {
        }

        private GlobalScope(
            QualifiedBinder Binder, ConversionRules ConversionRules,
            ICompilerLog Log, TypeConverterBase<string> TypeNamer,
            IDocumentationParser DocumentationParser,
            ThreadLocal<GlobalMemberCache> MemberCache,
            WarningStack Warnings)
        {
            this.Binder = Binder;
            this.ConversionRules = ConversionRules;
            this.Log = Log;
            this.TypeNamer = TypeNamer;
            this.DocumentationParser = DocumentationParser;
            this.memCache = MemberCache;
            this.extMemCache = new ThreadLocal<ExtensionMemberCache>(
                CreateExtensionMemberCache);
            this.warningStack = Warnings;
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

        /// <summary>
        /// Gets the documentation parser for this global scope.
        /// </summary>
        /// <value>The documentation parser.</value>
        public IDocumentationParser DocumentationParser { get; private set; }

        private WarningStack warningStack;

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

        /// <summary>
        /// Creates a new global scope that is identical to this scope except for its binder,
        /// which is given as an argument.
        /// </summary>
        /// <param name="NewBinder">The new binder.</param>
        /// <returns>The new global scope.</returns>
        public GlobalScope WithBinder(QualifiedBinder NewBinder)
        {
            return new GlobalScope(
                NewBinder, ConversionRules, Log, TypeNamer,
                DocumentationParser, memCache, warningStack);
        }

        /// <summary>
        /// Creates a new global scope that disables the warnings with the given names.
        /// </summary>
        /// <param name="WarningNames">The names of the warnings to disable.</param>
        /// <returns>The new global scope.</returns>
        public GlobalScope DisableWarnings(params string[] WarningNames)
        {
            return new GlobalScope(
                Binder, ConversionRules, Log, TypeNamer,
                DocumentationParser, memCache, warningStack.PushDisable(WarningNames)); 
        }

        /// <summary>
        /// Creates a new global scope that restores the warnings with the given names.
        /// </summary>
        /// <param name="WarningNames">The names of the warnings to restore.</param>
        /// <returns>The new global scope.</returns>
        public GlobalScope RestoreWarnings(params string[] WarningNames)
        {
            return new GlobalScope(
                Binder, ConversionRules, Log, TypeNamer,
                DocumentationParser, memCache, warningStack.PushRestore(WarningNames)); 
        }

        /// <summary>
        /// Tests if the given warning should be enabled in this global scope.
        /// </summary>
        /// <param name="Warning">A description of the warning.</param>
        /// <returns>A Boolean value that tells if the warning is enabled.</returns>
        public bool UseWarning(WarningDescription Warning)
        {
            if (warningStack.IsDisabled(Warning.WarningOption))
                return false;
            else
                return Warning.UseWarning(Log.Options);
        }

        private ExtensionMemberCache CreateExtensionMemberCache()
        {
            return new ExtensionMemberCache(Binder, memCache.Value);
        }
    }
}

