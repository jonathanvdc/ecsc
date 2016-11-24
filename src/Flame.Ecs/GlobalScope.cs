using System;
using System.Collections.Generic;
using System.Threading;
using Flame.Compiler;
using Flame.Build;
using Flame.Compiler.Expressions;
using Pixie;
using Flame.Ecs.Semantics;

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
                    new Dictionary<string, IVariable>()));
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

        private ConversionDescription PickAnyConversion(
            IReadOnlyList<ConversionDescription> Conversions)
        {
            if (Conversions.Count > 0)
                return Conversions[0];
            else
                return ConversionDescription.None;
        }

        private IExpression ApplyOrUnknown(
            IExpression From, IType To, 
            ConversionDescription Conversion)
        {
            if (Conversion.Kind != ConversionKind.None)
                return Conversion.Convert(From, To);
            else
                return new UnknownExpression(To);
        }

        private IExpression ApplyAnyConversion(
            IExpression From, IType To, 
            IReadOnlyList<ConversionDescription> Conversions)
        {
            return ApplyOrUnknown(From, To, PickAnyConversion(Conversions));
        }

        /// <summary>
        /// Gets an implicit conversion of the given expression to the given type.
        /// A diagnostic is issued if this is not a legal operation,
        /// but the resulting expression is always of the given target type,
        /// and is never null.
        /// </summary>
        public ConversionDescription GetImplicitConversion(
            IExpression From, IType To, SourceLocation Location)
        {
            var convs = ConversionRules.ClassifyConversion(From, To);

            var result = ConversionDescription.None;
            foreach (var item in convs)
            {
                if (item.IsImplicit)
                {
                    if (result.Kind != ConversionKind.None)
                    {
                        Log.LogError(new LogEntry(
                            "ambiguous implicit conversion", 
                            NodeHelpers.HighlightEven(
                                "the implicit conversion of type '", TypeNamer.Convert(From.Type), 
                                "' to '", TypeNamer.Convert(To), "' is ambiguous."),
                            Location));
                        return result;
                    }
                    else
                    {
                        result = item;
                    }
                }
            }

            if (result.Kind == ConversionKind.None)
            {
                Log.LogError(new LogEntry(
                    "no implicit conversion", 
                    NodeHelpers.HighlightEven(
                        "cannot implicitly convert type '", TypeNamer.Convert(From.Type), 
                        "' to '", TypeNamer.Convert(To), "'." +
                        (convs.Count > 0 ? " An explicit conversion exists. (are you missing a cast?)" : "")),
                    Location));

                return PickAnyConversion(convs);
            }
            else
            {
                return result;
            }
        }

        /// <summary>
        /// Implicitly converts the given expression to the given type.
        /// A diagnostic is issued if this is not a legal operation,
        /// but the resulting expression is always of the given target type,
        /// and is never null.
        /// </summary>
        public IExpression ConvertImplicit(IExpression From, IType To, SourceLocation Location)
        {
            return ApplyOrUnknown(From, To, GetImplicitConversion(From, To, Location));
        }

        /// <summary>
        /// Statically converts the given expression to the given type.
        /// A diagnostic is issued if this is not a legal operation,
        /// but the resulting expression is always of the given target type,
        /// and is never null.
        /// </summary>
        public IExpression ConvertStatic(IExpression From, IType To, SourceLocation Location)
        {
            var convs = ConversionRules.ClassifyConversion(From, To);

            IExpression result = null;
            foreach (var item in convs)
            {
                if (item.IsStatic)
                {
                    if (result != null)
                    {
                        Log.LogError(new LogEntry(
                            "ambiguous static conversion", 
                            NodeHelpers.HighlightEven(
                                "the static conversion of type '", TypeNamer.Convert(From.Type), 
                                "' to '", TypeNamer.Convert(To), "' is ambiguous."),
                            Location));
                        return result;
                    }
                    else
                    {
                        result = item.Convert(From, To);
                    }
                }
            }

            if (result == null)
            {
                Log.LogError(new LogEntry(
                    "no static conversion", 
                    NodeHelpers.HighlightEven(
                        "cannot guarantee at compile-time that type '", TypeNamer.Convert(From.Type), 
                        "' can safely be converted to '", TypeNamer.Convert(To), "'." +
                    (convs.Count > 0 ? " An explicit conversion exists." : "")),
                    Location));

                return ApplyAnyConversion(From, To, convs);
            }
            else
            {
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
            var convs = ConversionRules.ClassifyConversion(From, To);

            if (convs.Count == 1)
                // This is the usual fast-path.
                return convs[0].Convert(From, To);

            // Prefer explicit conversions over implicit conversions
            // here.
            IExpression result = null;
            foreach (var item in convs)
            {
                if (item.IsExplicit)
                {
                    if (result != null)
                    {
                        Log.LogError(new LogEntry(
                            "ambiguous explicit conversion", 
                            NodeHelpers.HighlightEven(
                                "the explicit conversion of type '", TypeNamer.Convert(From.Type), 
                                "' to '", TypeNamer.Convert(To), "' is ambiguous."),
                            Location));
                        return result;
                    }
                    else
                    {
                        result = item.Convert(From, To);
                    }
                }
            }

            if (convs.Count == 0)
            {
                Log.LogError(new LogEntry(
                    "no conversion", 
                    NodeHelpers.HighlightEven(
                        "cannot convert type '", TypeNamer.Convert(From.Type), 
                        "' to '", TypeNamer.Convert(To), "'."),
                    Location));

                return new UnknownExpression(To);
            }
            else
            {
                Log.LogError(new LogEntry(
                    "ambiguous explicit conversion", 
                    NodeHelpers.HighlightEven(
                        "the explicit conversion of type '", TypeNamer.Convert(From.Type), 
                        "' to '", TypeNamer.Convert(To), "' is ambiguous, because " +
                    "there no true explicit conversion was applicable, and the implicit " +
                    "conversion was ambiguous."),
                    Location));
                return ApplyAnyConversion(From, To, convs);
            }
        }
    }
}

