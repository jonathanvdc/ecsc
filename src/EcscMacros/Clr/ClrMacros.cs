using System;
using LeMP;
using Loyc.Syntax;
using Loyc;

namespace EcscMacros.Clr
{
    [ContainsMacros]
    public static class ClrMacros
    {
        static LNodeFactory F = new LNodeFactory(new EmptySourceFile("ClrMacros.cs"));

        private static LNode Reject(IMessageSink sink, LNode at, string msg)
        {
            sink.Write(Severity.Error, at, msg);
            return null;
        }

        [LexicalMacro(
            "#delegate(TRet, DelegateName, #(#var(TArg, x)...))",
            "lowers a delegate definition to a class definition",
            "#delegate",
            Mode = MacroMode.Passive | MacroMode.Normal)]
        public static LNode LowerDelegateDefinition(LNode Node, IMacroContext Sink)
        {
            if (!Node.Calls(CodeSymbols.Delegate, 3))
            {
                return Reject(Sink, Node, "#delegate' takes exactly three arguments.");
            }

            // For the CLR environment, we want to rewrite
            //
            //     [attrs...] #delegate(TRet, DelegateName, #(#var(TArg, x)...))
            //
            // as
            //
            //     [#builtin_attribute(DelegateAttribute, "Invoke")]
            //     [#builtin_attribute(CompileAtomicallyAttribute)]
            //     [attrs...]
            //     sealed class DelegateName : System.MulticastDelegate
            //     {
            //         [#builtin_attribute(RuntimeImplementedAttribute)]
            //         public DelegateName(object @object, System.IntPtr funcPtr);
            //
            //         [#builtin_attribute(RuntimeImplementedAttribute)]
            //         public TRet Invoke(TArg x, ...);
            //
            //         [#builtin_attribute(RuntimeImplementedAttribute)]
            //         public System.IAsyncResult BeginInvoke(TArg x, ..., System.AsyncCallback callback, object @object);
            //
            //         [#builtin_attribute(RuntimeImplementedAttribute)]
            //         public TRet EndInvoke(System.IAsyncResult result);
            //     }

            var retType = Node.Args[0];
            var name = Node.Args[1];
            var paramList = Node.Args[2];

            var rtImplAttribute = F.Call(
                EcscSymbols.BuiltinAttribute,
                F.Id("RuntimeImplementedAttribute"));

            var delegateAttribute = F.Call(
                EcscSymbols.BuiltinAttribute,
                F.Id("DelegateAttribute"),
                F.Literal("Invoke"));

            var compileAtomicallyAttribute = F.Call(
                EcscSymbols.BuiltinAttribute,
                F.Id("CompileAtomicallyAttribute"));

            var asyncResult = F.Dot("System", "IAsyncResult");
            var asyncCallback = F.Dot("System", "AsyncCallback");
            var multicastDelegate = F.Dot("System", "MulticastDelegate");
            var intPtr = F.Dot("System", "IntPtr");

            var ctor = F.Call(
                CodeSymbols.Constructor,
                F.Id(GSymbol.Empty),
                name,
                F.List(
                    F.Var(F.Id(CodeSymbols.Object), "object"),
                    F.Var(intPtr, "funcPtr")))
                .PlusAttrs(
                    rtImplAttribute,
                    F.Id(CodeSymbols.Public));

            var invoke = F.Call(
                CodeSymbols.Fn,
                retType,
                F.Id("Invoke"),
                paramList)
                .PlusAttrs(
                    rtImplAttribute,
                    F.Id(CodeSymbols.Public));

            var beginInvoke = F.Call(
                CodeSymbols.Fn,
                asyncResult,
                F.Id("BeginInvoke"),
                paramList.PlusArgs(
                    F.Var(asyncCallback, "callback"),
                    F.Var(F.Id(CodeSymbols.Object), "object")))
                .PlusAttrs(
                    rtImplAttribute,
                    F.Id(CodeSymbols.Public));

            var endInvoke = F.Call(
                CodeSymbols.Fn,
                retType,
                F.Id("EndInvoke"),
                F.List(
                    F.Var(asyncResult, "result")))
                .PlusAttrs(
                    rtImplAttribute,
                    F.Id(CodeSymbols.Public));

            return F.Call(
                CodeSymbols.Class,
                name,
                F.List(multicastDelegate),
                F.Braces(
                    ctor,
                    invoke,
                    beginInvoke,
                    endInvoke))
                .PlusAttrs(
                    delegateAttribute,
                    compileAtomicallyAttribute)
                .PlusAttrs(Node.Attrs)
                .PlusAttr(F.Id(CodeSymbols.Sealed));
        }
    }
}

