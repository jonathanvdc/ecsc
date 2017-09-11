using System;
using Loyc;

namespace EcscMacros
{
    /// <summary>
    /// Additional symbols for the ecsc compiler.
    /// </summary>
    public static class EcscSymbols
    {
        /// <summary>
        /// A builtin attribute node type that marks its parent node as hidden.
        /// Hidden members are not directly inaccessible from user code, but
        /// can be accessed by the compiler.
        /// </summary>
        /// <remarks>
        /// Usage:
        ///     [#builtin_hidden] public static string StringFromConstPtr(byte* ptr) { ... }
        /// </remarks>
        public static readonly Symbol BuiltinHidden = GSymbol.Get("#builtin_hidden");

        /// <summary>
        /// A builtin node type that produces the declaring type of its argument.
        /// </summary>
        /// <remarks>
        /// Usage: #builtin_decltype(18);
        /// </remarks>
        public static readonly Symbol BuiltinDecltype = GSymbol.Get("#builtin_decltype");

        /// <summary>
        /// A builtin node type that tries to evaluate a condition at compile-time.
        /// One out of two nodes is then analyzed based on this condition. 
        /// If the condition cannot be evaluated at compile-time, an error is produced.
        /// </summary>
        /// <remarks>
        /// Usage: #builtin_static_if(cond, if_node, else_node);
        /// </remarks>
        public static readonly Symbol BuiltinStaticIf = GSymbol.Get("#builtin_static_if");

        /// <summary>
        /// A builtin node type that tests if the first argument is a subtype
        /// of the second argument.
        /// </summary>
        /// <remarks>
        /// Usage: #builtin_static_is(type1, type2);
        /// </remarks>
        public static readonly Symbol BuiltinStaticIs = GSymbol.Get("#builtin_static_is");

        /// <summary>
        /// A builtin node that disables specific types of warnings.
        /// </summary>
        /// <returns>
        /// Usage: #builtin_warning_disable("Wfirst", "Wsecond", body);
        /// </returns>
        public static readonly Symbol BuiltinWarningDisable = GSymbol.Get("#builtin_warning_disable");

        /// <summary>
        /// A builtin node that restores specific types of warnings.
        /// </summary>
        /// <returns>
        /// Usage: #builtin_warning_restore("Wfirst", "Wsecond", body);
        /// </returns>
        public static readonly Symbol BuiltinWarningRestore = GSymbol.Get("#builtin_warning_restore");

        /// <summary>
        /// A builtin node type that tests if a type is an array. An optional array rank can be
        /// given.
        /// </summary>
        /// <remarks>
        /// Usage: #builtin_static_is_array(type);
        ///        #builtin_static_is_array(type, rank);
        /// </remarks>
        public static readonly Symbol BuiltinStaticIsArray = GSymbol.Get("#builtin_static_is_array");

        /// <summary>
        /// A builtin node type that converts a reference to a pointer to its data.
        /// </summary>
        /// <remarks>
        /// Usage: #builtin_ref_to_ptr(reference);
        /// </remarks>
        public static readonly Symbol BuiltinRefToPtr = GSymbol.Get("#builtin_ref_to_ptr");

        /// <summary>
        /// A builtin node type that defines an intrinsic attribute.
        /// </summary>
        /// <returns>
        /// Usage: #builtin_attribute(SomeAttribute, args...);
        /// </returns>
        public static readonly Symbol BuiltinAttribute = GSymbol.Get("#builtin_attribute");

        /// <summary>
        /// Disposes a local variable, if it implements IDisposable.
        /// </summary>
        public static readonly Symbol DisposeLocal = GSymbol.Get("#dispose_local");

        /// <summary>
        /// Disposes a value, if it implements IDisposable.
        /// </summary>
        public static readonly Symbol DisposeValue = GSymbol.Get("#dispose_value");

        /// <summary>
        /// A documentation comment.
        /// </summary>
        public static readonly Symbol TriviaDocumentationComment = GSymbol.Get("#trivia_doc_comment");
    }
}

