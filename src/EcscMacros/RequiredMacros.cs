using System;
using LeMP;
using Loyc.Syntax;
using Loyc;

namespace EcscMacros
{
    [ContainsMacros]
    public class RequiredMacros
    {
        public RequiredMacros()
        {
        }

        static LNodeFactory F = new LNodeFactory(new EmptySourceFile("RequiredMacros.cs"));

        public static LNode EcscPrologue = F.Call(
            GSymbol.Get("#importMacros"),
            F.Id("EcscMacros"));

        static LNode Reject(IMessageSink sink, LNode at, string msg)
        {
            sink.Write(Severity.Error, at, msg);
            return null;
        }

        private static LNode ExpandArrayForeach(
            LNode InductionType, Symbol InductionName,
            LNode Collection, LNode Body)
        {
            // Produce a Loyc tree that looks like this:
            //
            //     #builtin_stash_names(col, colLen, i, {
            //         var col = <collection>;
            //         var colLen = col.Length;
            //         for (#builtin_decltype(colLen) i = 0; i < colLen; i++)
            //         {
            //             #var(<type>, <name> = col[i]);
            //             #builtin_restore_names(col, colLen, i, <body>);
            //         }
            //     });
            //
            var colName = (Symbol)"col";
            var colLenName = (Symbol)"colLen";
            var iName = (Symbol)"i";
            return F.Call(
                EcscSymbols.BuiltinStashLocals, 
                F.Id(colName), F.Id(colLenName), F.Id(iName), 
                F.Braces(
                    F.Var(
                        F.Id(GSymbol.Empty), 
                        colName, 
                        Collection),
                    F.Var(
                        F.Id(GSymbol.Empty), 
                        colLenName, 
                        F.Dot(colName, (Symbol)"Length")),
                    F.Call(
                        CodeSymbols.For,
                        F.Tuple(F.Var(
                            F.Call(
                                EcscSymbols.BuiltinDecltype, 
                                F.Id(colLenName)),
                            iName, 
                            F.Literal(0))),
                        F.Call(
                            CodeSymbols.LT, 
                            F.Id(iName), F.Id(colLenName)),
                        F.Tuple(F.Call(
                            CodeSymbols.PostInc,
                            F.Id(iName))),
                        F.Braces(
                            F.Var(
                                InductionType,
                                InductionName,
                                F.Call(CodeSymbols.IndexBracks, F.Id(colName), F.Id(iName))),
                            F.Call(
                                EcscSymbols.BuiltinRestoreLocals,
                                F.Id(colName), F.Id(colLenName), F.Id(iName),
                                Body)))));
        }

        private static LNode ExpandDefaultForeach(
            LNode InductionType, Symbol InductionName,
            LNode Collection, LNode Body)
        {
            // Produce a Loyc tree that looks like this:
            //
            //     #builtin_stash_names(enumerator, {
            //         var enumerator = <collection>.GetEnumerator();
            //         try
            //         {
            //             while (enumerator.MoveNext())
            //             {
            //                 #var(<type>, <name> = enumerator.Current);
            //                 #builtin_restore_names(enumerator, <body>);
            //             }
            //         }
            //         finally
            //         {
            //             #dispose_local(enumerator);
            //         }
            //     });
            //
            var enumeratorName = (Symbol)"enumerator";

            return F.Call(
                EcscSymbols.BuiltinStashLocals,
                F.Id(enumeratorName),
                F.Braces(
                    F.Var(
                        F.Id(GSymbol.Empty), 
                        enumeratorName, 
                        F.Call(F.Dot(Collection, (Symbol)"GetEnumerator"))),
                    F.Call(
                        CodeSymbols.Try,
                        F.Braces(
                            F.Call(
                                CodeSymbols.While,
                                F.Call(F.Dot(enumeratorName, (Symbol)"MoveNext")),
                                F.Braces(
                                    F.Var(
                                        InductionType, 
                                        InductionName, 
                                        F.Dot(enumeratorName, (Symbol)"Current")),
                                    F.Call(
                                        EcscSymbols.BuiltinRestoreLocals,
                                        F.Id(enumeratorName),
                                        Body)))),
                        F.Call(
                            CodeSymbols.Finally,
                            F.Braces(
                                F.Call(
                                    EcscSymbols.DisposeLocal, 
                                    F.Id(enumeratorName)))))));
        }

        [LexicalMacro("foreach (type name in collection) body;", "macro-expanded instead of implemented directly", "#foreach")]
        public static LNode Foreach(LNode Node, IMacroContext Sink)
        {
            // A #foreach(#var(<type>, <name>), <collection>, <body>)
            // is converted to:
            //
            // #builtin_static_if(#builtin_static_is_array(#builtin_decltype(<collection>), 1), {
            //     (special 'foreach' loop for arrays)
            // }, {
            //     (general 'foreach' loop)
            // })

            if (Node.ArgCount != 3)
                return Reject(Sink, Node, "'#foreach' must take exactly three arguments.");

            var varNode = Node.Args[0];
            if (!varNode.Calls(CodeSymbols.Var, 2))
                return Reject(Sink, Node, "the first argument to '#foreach' must be a call to '#var' with two arguments.");

            var indTy = varNode.Args[0];

            if (!varNode.Args[1].IsId)
                return Reject(Sink, Node, "the second argument to the '#var' in a '#foreach' must be an identifier.");

            var indName = varNode.Args[1].Name;
            var col = Node.Args[1];
            var body = Node.Args[2];

            return F.Call(
                EcscSymbols.BuiltinStaticIf, 
                F.Call(
                    EcscSymbols.BuiltinStaticIsArray,
                    F.Call(
                        EcscSymbols.BuiltinDecltype,
                        col),
                    F.Literal(1)),
                ExpandArrayForeach(indTy, indName, col, body),
                ExpandDefaultForeach(indTy, indName, col, body));
        }

        [LexicalMacro("#dispose_value(value);", "disposes a value if it implements IDisposable", "#dispose_value")]
        public static LNode DisposeValue(LNode Node, IMacroContext Sink)
        {
            // Given this Loyc tree:
            //
            //     #dispose_value(Value);
            //
            // we will produce a Loyc tree that looks like this:
            //
            //      #builtin_stash_locals(temp, {
            //          var temp = Value;
            //          #dispose_local(temp);
            //      });
            //

            if (!Node.Calls(EcscSymbols.DisposeValue, 1))
                return Reject(Sink, Node, "'#dispose_value' nodes must take exactly one argument.");

            var value = Node.Args[0];
            var tempName = (Symbol)"temp";
            return F.Call(
                EcscSymbols.BuiltinStashLocals,
                F.Id(tempName),
                F.Braces(
                    F.Var(F.Id(GSymbol.Empty), tempName, value),
                    F.Call(EcscSymbols.DisposeLocal, F.Id(tempName))));
        }

        [LexicalMacro("#dispose_local(value);", "disposes a local variable if it implements IDisposable", "#dispose_local")]
        public static LNode DisposeLocal(LNode Node, IMacroContext Sink)
        {
            // Given this Loyc tree:
            //
            //     #dispose_local(Value);
            //
            // we will produce a Loyc tree that looks like this:
            //
            //      if (Value is System.IDisposable)
            //          ((System.IDisposable)Value).Dispose();
            //
            // Note: this intentionally does boxing for value types,
            // because 'foreach' has the same behavior (and 'foreach'
            // uses this macro).

            if (!Node.Calls(EcscSymbols.DisposeLocal, 1))
                return Reject(Sink, Node, "'#dispose_local' nodes must take exactly one argument.");

            var value = Node.Args[0];
            var idisposableNode = F.Dot("System", "IDisposable");
            return F.Call(
                CodeSymbols.If,
                F.Call(
                    CodeSymbols.Is,
                    value,
                    idisposableNode),
                F.Call(
                    F.Dot(
                        F.Call(
                            CodeSymbols.Cast,
                            value,
                            idisposableNode),
                        (Symbol)"Dispose")));
        }
    }
}

