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
        /// A builtin node type that tests if a type is an array. An optional array rank can be
        /// given.
        /// </summary>
        /// <remarks>
        /// Usage: #builtin_static_is_array(type);
        ///        #builtin_static_is_array(type, rank);
        /// </remarks>
        public static readonly Symbol BuiltinStaticIsArray = GSymbol.Get("#builtin_static_is_array");

        /// <summary>
        /// Stashes a number of names in the local context of a node. 
        /// </summary>
        /// <remarks>
        /// Usage: #builtin_stash_names(x, y, z, expr);
        /// </remarks>
        public static readonly Symbol BuiltinStashNames = GSymbol.Get("#builtin_stash_names");

        /// <summary>
        /// Restores stashed names from the local context of a node.
        /// </summary>
        /// <remarks>
        /// Usage: #builtin_restore_names(x, y, z, expr);
        /// </remarks>
        public static readonly Symbol BuiltinRestoreNames = GSymbol.Get("#builtin_restore_names");
    }
}

