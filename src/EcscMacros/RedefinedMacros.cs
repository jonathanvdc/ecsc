using System;
using LeMP;
using Loyc.Syntax;
using Loyc;

namespace EcscMacros
{
    [ContainsMacros]
    public class RedefinedMacros
    {
        public RedefinedMacros()
        {
        }

        static LNodeFactory F = new LNodeFactory(new EmptySourceFile("RedefinedMacros.cs"));

        [LexicalMacro(@"static if() {...} else {...}", "This implementation of static if maps to #builtin_static_if", "#if",
            Mode = MacroMode.Passive | MacroMode.PriorityInternalOverride)]
        public static LNode StaticIf(LNode @if, IMacroContext context)
        {
            LNode @static;
            if ((@static = @if.AttrNamed(CodeSymbols.Static)) == null || !@static.IsId)
                return null;
            return static_if(@if, context);
        }

        [LexicalMacro(@"static_if(cond, then, otherwise)", "This implementation of static_if maps to #builtin_static_if",
            Mode = MacroMode.Passive | MacroMode.PriorityInternalOverride)]
        public static LNode static_if(LNode @if, IMacroContext context)
        {
            int argCount = @if.ArgCount;
            if (argCount == 2)
                return F.Call(EcscSymbols.BuiltinStaticIf, @if.Args[0], @if.Args[1], F.Braces());
            else if (argCount == 3)
                return F.Call(EcscSymbols.BuiltinStaticIf, @if.Args);
            else
                return null;
        }
    }
}
