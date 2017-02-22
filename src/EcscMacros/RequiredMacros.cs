using System;
using LeMP;
using Loyc.Syntax;
using Loyc;
using System.Collections.Generic;
using Loyc.Collections;

namespace EcscMacros
{
    [ContainsMacros]
    public class RequiredMacros
    {
        public RequiredMacros()
        {
        }

        static LNodeFactory F = new LNodeFactory(new EmptySourceFile("RequiredMacros.cs"));

        /// <summary>
        /// Gets the sequence of prologue nodes for EC#.
        /// </summary>
        public static IEnumerable<LNode> EcscPrologue = new LNode[]
        {
            F.Call(
                GSymbol.Get("#importMacros"),
                F.Id("EcscMacros"))
        };

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
            //     var col = <collection>;
            //     var colLen = col.Length;
            //     for (#builtin_decltype(colLen) i = 0; i < colLen; i++)
            //     {
            //         #var(<type>, <name> = col[i]);
            //         <body>;
            //     }
            //
            var pool = new SymbolPool();
            var colName = pool.Get("col");
            var colLenName = pool.Get("colLen");
            var iName = pool.Get("i");
            return F.Braces(
                F.Var(
                    F.Id(GSymbol.Empty), 
                    colName, 
                    Collection),
                F.Var(
                    F.Id(GSymbol.Empty), 
                    colLenName, 
                    F.Dot(colName, GSymbol.Get("Length"))),
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
                        Body)));
        }

        private static LNode ExpandDefaultForeach(
            LNode InductionType, Symbol InductionName,
            LNode Collection, LNode Body)
        {
            // Produce a Loyc tree that looks like this:
            //
            //    var enumerator = <collection>.GetEnumerator();
            //    try
            //    {
            //         while (enumerator.MoveNext())
            //         {
            //             #var(<type>, <name> = enumerator.Current);
            //             <body>;
            //         }
            //     }
            //     finally
            //     {
            //         #dispose_local(enumerator);
            //     }
            //
            var pool = new SymbolPool();
            var enumeratorName = pool.Get("enumerator");

            return F.Braces(
                F.Var(
                    F.Id(GSymbol.Empty), 
                    enumeratorName, 
                    F.Call(F.Dot(
                        Collection, 
                        GSymbol.Get("GetEnumerator")))),
                F.Call(
                    CodeSymbols.Try,
                    F.Braces(
                        F.Call(
                            CodeSymbols.While,
                            F.Call(F.Dot(enumeratorName, GSymbol.Get("MoveNext"))),
                            F.Braces(
                                F.Var(
                                    InductionType, 
                                    InductionName, 
                                    F.Dot(enumeratorName, GSymbol.Get("Current"))),
                                Body))),
                    F.Call(
                        CodeSymbols.Finally,
                        F.Braces(
                            F.Call(
                                EcscSymbols.DisposeLocal, 
                                F.Id(enumeratorName))))));
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
            var pool = new SymbolPool();
            var tempName = pool.Get("temp");
            return F.Braces(
                F.Var(F.Id(GSymbol.Empty), tempName, value),
                F.Call(EcscSymbols.DisposeLocal, F.Id(tempName)));
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
                        GSymbol.Get("Dispose"))));
        }

        [LexicalMacro(
            "#var(T, variable = #arrayInit(values...))", 
            "lowers array initializers to new-array expressions", 
            "#var", Mode = MacroMode.Passive | MacroMode.Normal)]
        public static LNode LowerArrayInitializer(LNode Node, IMacroContext Sink)
        {
            // We want to match syntax trees that look like this:
            //
            // #var(T, variable = #arrayInit(values...))
            //
            // and then transform them to:
            //
            // #var(T, variable = #new(T, values...))

            if (!Node.CallsMin(CodeSymbols.Var, 1))
                return Reject(Sink, Node, "'#var' nodes must take at least one argument.");

            var varType = Node.Args[0];
            var newArgs = new VList<LNode>();
            newArgs.Add(varType);
            foreach (var item in Node.Args.Slice(1))
            {
                if (item.Calls(CodeSymbols.Assign, 2))
                {
                    var rhs = item.Args[1];
                    if (rhs.Calls(CodeSymbols.ArrayInit))
                    {
                        var lhs = item.Args[0];
                        newArgs.Add(
                            item.WithArgs(
                                lhs,
                                F.Call(CodeSymbols.New, F.Call(varType))
                                .PlusArgs(rhs.Args)
                                .WithRange(rhs.Range)));
                        continue;
                    }
                }
                newArgs.Add(item);
            }
            return Node.WithArgs(newArgs);
        }
    }
}

