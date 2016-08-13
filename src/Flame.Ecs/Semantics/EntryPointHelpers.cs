using System;
using System.Collections.Generic;
using System.Linq;
using Flame.Compiler;
using Pixie;

namespace Flame.Ecs.Semantics
{
    public static class EntryPointHelpers
    {
        /// <summary>
        /// Infers an assembly's entry point, 
        /// which is any function that matches the pattern
        /// `static void main(string[] Args)`.
        /// </summary>
        /// <param name="Assembly"></param>
        /// <returns></returns>
        public static IMethod InferEntryPoint(IAssembly Assembly, ICompilerLog Log)
        {
            IMethod result = null;
            foreach (var type in Assembly.CreateBinder().GetTypes())
            {
                foreach (var method in type.GetMethods())
                {
                    // Basically match anything that looks like `static void main(...)`
                    var name = method.Name as SimpleName;
                    if (name != null && name.Name.Equals("main", StringComparison.OrdinalIgnoreCase))
                    {
                        if (name.TypeParameterCount == 0 && method.IsStatic
                            && method.ReturnType.Equals(PrimitiveTypes.Void)
                            && !method.DeclaringType.GetRecursiveGenericParameters().Any())
                        {
                            if (result != null)
                            {
                                Log.LogError(new LogEntry(
                                    "multiple entry points",
                                    new MarkupNode[]
                                    { 
                                        new MarkupNode(NodeConstants.TextNodeType, "this program has more than one entry point."),
                                        method.GetSourceLocation().CreateDiagnosticsNode(),
                                        result.GetSourceLocation().CreateRemarkDiagnosticsNode("other entry point: ")
                                    }));
                            }
                            result = method;
                        }
                        else
                        {
                            WarningDescription warn;
                            string message;

                            if (name.TypeParameterCount > 0)
                            {
                                warn = EcsWarnings.GenericMainSignatureWarning;
                                message = "cannot be an entry point, because it is generic.";
                            }
                            else if (method.DeclaringType.GetRecursiveGenericParameters().Any())
                            {
                                warn = EcsWarnings.GenericMainSignatureWarning;
                                message = "cannot be an entry point, because its declaring type is generic.";
                            }
                            else if (!method.IsStatic)
                            {
                                warn = EcsWarnings.InvalidMainSignatureWarning;
                                message = "is an instance method, and cannot be an entry point.";
                            }
                            else
                            {
                                warn = EcsWarnings.InvalidMainSignatureWarning;
                                message = "has the wrong signature to be an entry point.";
                            }

                            if (warn.UseWarning(Log.Options))
                            {
                                Log.LogWarning(new LogEntry(
                                    "invalid main signature",
                                    NodeHelpers.HighlightEven(
                                        "'", name.ToString(),
                                        "' " + message + " ")
                                    .Concat(new MarkupNode[] { warn.CauseNode }),
                                    method.GetSourceLocation()));
                            }
                        }
                    }
                }
            }
            return result;
        }
    }
}

