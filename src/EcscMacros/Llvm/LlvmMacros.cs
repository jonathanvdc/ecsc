using System;
using LeMP;
using Loyc;
using Loyc.Syntax;

namespace EcscMacros.Llvm
{
    [ContainsMacros]
    public static class LlvmMacros
    {
        static LNodeFactory F = new LNodeFactory(new EmptySourceFile("LlvmMacros.cs"));

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

            // For the LLVM environment, we want to rewrite
            //
            //     [attrs...] #delegate(TRet, DelegateName, #(#var(TArg, x)...))
            //
            // as
            //
            //     [#builtin_attribute(DelegateAttribute, "Invoke")]
            //     [#builtin_attribute(RuntimeImplementedAttribute)]
            //     [attrs...]
            //     sealed class DelegateName
            //     {
            //         private DelegateName() { }
            //
            //         [#builtin_attribute(RuntimeImplementedAttribute)]
            //         public TRet Invoke(TArg x, ...);
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

            var ctor = F.Call(
                CodeSymbols.Constructor,
                F.Id(GSymbol.Empty),
                F.Id(CodeSymbols.This),
                F.List(),
                F.Braces())
                .PlusAttr(F.Id(CodeSymbols.Private));

            var invoke = F.Call(
                CodeSymbols.Fn,
                retType,
                F.Id("Invoke"),
                paramList)
                .PlusAttrs(
                    rtImplAttribute,
                    F.Id(CodeSymbols.Public));

            return F.Call(
                CodeSymbols.Class,
                name,
                F.List(),
                F.Braces(
                    ctor,
                    invoke))
                .PlusAttrs(
                    delegateAttribute,
                    rtImplAttribute)
                .PlusAttrs(Node.Attrs)
                .PlusAttr(F.Id(CodeSymbols.Sealed));
        }
    }
}

