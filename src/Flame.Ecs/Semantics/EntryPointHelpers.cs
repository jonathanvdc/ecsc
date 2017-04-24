using System;
using System.Collections.Generic;
using System.Linq;
using Flame.Compiler;
using Pixie;

namespace Flame.Ecs.Semantics
{
    /// <summary>
    /// Contains functionality that detects entry points.
    /// </summary>
    public static class EntryPointHelpers
    {
        // The C# spec on entry points:
        //
        // Application startup occurs when the execution environment calls a designated
        // method, which is referred to as the application's entry point. This entry
        // point method is always named Main, and can have one of the following
        // signatures:
        //
        //     static void Main() {...}
        //
        //     static void Main(string[] args) {...}
        //
        //     static int Main() {...}
        //
        //     static int Main(string[] args) {...}
        //
        // As shown, the entry point may optionally return an int value. This return
        // value is used in application termination (Application termination).
        //
        // The entry point may optionally have one formal parameter. The parameter may
        // have any name, but the type of the parameter must be string[]. If the formal
        // parameter is present, the execution environment creates and passes a string[]
        // argument containing the command-line arguments that were specified when the
        // application was started. The string[] argument is never null, but it may have a
        // length of zero if no command-line arguments were specified.
        //
        // Since C# supports method overloading, a class or struct may contain multiple
        // definitions of some method, provided each has a different signature. However,
        // within a single program, no class or struct may contain more than one method
        // called Main whose definition qualifies it to be used as an application entry
        // point. Other overloaded versions of Main are permitted, however, provided
        // they have more than one parameter, or their only parameter is other than type
        // string[].
        //
        // An application can be made up of multiple classes or structs. It is possible
        // for more than one of these classes or structs to contain a method called Main
        // whose definition qualifies it to be used as an application entry point. In such
        // cases, an external mechanism (such as a command-line compiler option) must be
        // used to select one of these Main methods as the entry point.
        //
        // In C#, every method must be defined as a member of a class or struct.
        // Ordinarily, the declared accessibility (Declared accessibility) of a method is
        // determined by the access modifiers (Access modifiers) specified in its declaration,
        // and similarly the declared accessibility of a type is determined by the access
        // modifiers specified in its declaration. In order for a given method of a given type
        // to be callable, both the type and the member must be accessible. However, the
        // application entry point is a special case. Specifically, the execution
        // environment can access the application's entry point regardless of its declared
        // accessibility and regardless of the declared accessibility of its enclosing type
        // declarations.
        //
        // The application entry point method may not be in a generic class declaration.

        /// <summary>
        /// Gets the name of an entry point.
        /// </summary>
        public const string EntryPointMethodName = "Main";

        private static readonly HashSet<IType> entryPointReturnTypes = new HashSet<IType>()
        {
            PrimitiveTypes.Int32,
            PrimitiveTypes.Void
        };

        private static readonly HashSet<TypeSequence> entryPointParameterLists = new HashSet<TypeSequence>()
        {
            new TypeSequence(Enumerable.Empty<IType>()),
            new TypeSequence(new IType[] { PrimitiveTypes.String.MakeArrayType(1) })
        };

        /// <summary>
        /// Tests if the given name can be the name of an entry point method.
        /// </summary>
        /// <param name="Name">The name to examine.</param>
        /// <returns><c>true</c> if a method with the given name could be an entry point; otherwise, <c>false</c>.</returns>
        public static bool IsEntryPointName(UnqualifiedName Name)
        {
            var simpleName = Name as SimpleName;
            return simpleName != null
                && simpleName.Name.Equals(EntryPointMethodName, StringComparison.Ordinal);
        }

        /// <summary>
        /// Tests if the given type could be the return type of an entry point.
        /// </summary>
        /// <param name="ReturnType">The type to examine.</param>
        /// <returns><c>true</c> if a method with the given return type could be an entry point; otherwise, <c>false</c>.</returns>
        public static bool IsEntryPointReturnType(IType ReturnType)
        {
            return entryPointReturnTypes.Contains(ReturnType);
        }

        /// <summary>
        /// Tests if the given parameter list could be the parameter list of an entry point.
        /// </summary>
        /// <param name="ParameterList">The parameter list of an entry point.</param>
        /// <returns><c>true</c> if a method with the given parameter list could be an entry point; otherwise, <c>false</c>.</returns>
        public static bool IsEntryPointParameterList(IEnumerable<IParameter> ParameterList)
        {
            return entryPointParameterLists.Contains(
                new TypeSequence(ParameterList.Select(param => param.ParameterType).ToArray()));
        }

        /// <summary>
        /// Tests if the given method has the right signature to be an entry point.
        /// </summary>
        /// <param name="Method">The method to examine.</param>
        /// <returns>
        /// <c>true</c> if the given method could be an entry point; otherwise, <c>false</c>.
        /// </returns>
        public static bool HasEntryPointSignature(IMethod Method)
        {
            return IsEntryPointName(Method.Name)
                && Method.IsStatic
                && IsEntryPointReturnType(Method.ReturnType)
                && IsEntryPointParameterList(Method.Parameters)
                && !Method.GetRecursiveGenericParameters().Any();
        }

        /// <summary>
        /// Infers the entry point of a C# assembly.
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
                    if (IsEntryPointName(method.Name))
                    {
                        if (HasEntryPointSignature(method))
                        {
                            if (result != null)
                            {
                                Log.LogError(new LogEntry(
                                    "multiple entry points",
                                    new MarkupNode[]
                                    { 
                                        new MarkupNode(
                                            NodeConstants.TextNodeType,
                                            "the program has more than one entry point."),
                                        method.GetSourceLocation().CreateDiagnosticsNode(),
                                        result.GetSourceLocation().CreateRemarkDiagnosticsNode(
                                            "other entry point: ")
                                    }));
                            }
                            result = method;
                        }
                        else
                        {
                            WarningDescription warn;
                            string message;

                            if (method.GenericParameters.Any())
                            {
                                warn = EcsWarnings.GenericMainSignatureWarning;
                                message = "cannot be an entry point because it is generic.";
                            }
                            else if (method.DeclaringType.GetRecursiveGenericParameters().Any())
                            {
                                warn = EcsWarnings.GenericMainSignatureWarning;
                                message = "cannot be an entry point because its declaring type is generic.";
                            }
                            else if (!method.IsStatic)
                            {
                                warn = EcsWarnings.InvalidMainSignatureWarning;
                                message = "is an instance method and cannot be an entry point.";
                            }
                            else if (!IsEntryPointReturnType(method.ReturnType))
                            {
                                warn = EcsWarnings.InvalidMainSignatureWarning;
                                message = "has a return type that disqualifies it from being an entry point. " +
                                    "Allowed entry point return types are 'int' and 'void'.";
                            }
                            else if (!IsEntryPointParameterList(method.Parameters))
                            {
                                warn = EcsWarnings.InvalidMainSignatureWarning;
                                message = "has a parameter list that disqualifies it from being an entry point. " +
                                    "Allowed entry point parameter lists are the empty parameter list and a single " + 
                                    "parameter of type 'string[]'.";
                            }
                            else
                            {
                                warn = EcsWarnings.InvalidMainSignatureWarning;
                                message = "is called '" + EntryPointMethodName +
                                    "' but doesn't have the right signature to be an entry point.";
                            }

                            if (warn.UseWarning(Log.Options))
                            {
                                Log.LogWarning(new LogEntry(
                                    "invalid main signature",
                                    NodeHelpers.HighlightEven(
                                        "method '", method.Name.ToString(),
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

