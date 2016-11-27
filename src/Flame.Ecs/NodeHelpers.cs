using Loyc;
using Loyc.Syntax;
using Flame;
using Flame.Compiler;
using System;
using System.Collections.Generic;
using System.Linq;
using Pixie;
using Flame.Build;

namespace Flame.Ecs
{
    public static class NodeHelpers
    {
        /// <summary>
        /// Converts the given Loyc `SourceRange` to a Flame `SourceLocation`.
        /// </summary>
        public static SourceLocation ToSourceLocation(SourceRange Range)
        {
            var doc = new LoycSourceDocument(Range.Source);
            return new SourceLocation(doc, Range.StartIndex, Range.Length);
        }

        /// <summary>
        /// Converts the given node to a qualified name.
        /// </summary>
        public static QualifiedName ToQualifiedName(LNode Node)
        {
            var name = Node.Name;
            if (Node.IsCall && (name == CodeSymbols.Dot || name == CodeSymbols.ColonColon))
            {
                var left = ToQualifiedName(Node.Args[0]);
                var right = ToQualifiedName(Node.Args[1]);
                return left.IsEmpty || right.IsEmpty 
                    ? default(QualifiedName) 
                    : right.Qualify(left);
            }
            else if (Node.IsId)
                return new QualifiedName(name.Name);
            else
                return default(QualifiedName);
        }

        /// <summary>
        /// Produces an array of markup nodes, where the 
        /// even arguments are highlighted.
        /// </summary>
        public static MarkupNode[] HighlightEven(params string[] Text)
        {
            var results = new MarkupNode[Text.Length];
            for (int i = 0; i < Text.Length; i++)
            {
                results[i] = new MarkupNode(
                    i % 2 == 0 ? NodeConstants.TextNodeType : NodeConstants.BrightNodeType,
                    Text[i]);
            }
            return results;
        }

        /// <summary>
        /// Creates a list of markup nodes that speak of
        /// of a redefinition. A message, the new definition
        /// and the original definition are presented.
        /// </summary>
        /// <returns>The redefinition message.</returns>
        /// <param name="Message">The message to log.</param>
        /// <param name="NewDefinition">The location of the new definition.</param>
        /// <param name="OriginalDefinition">The location of the original definition.</param>
        public static MarkupNode[] CreateRedefinitionMessage(
            IEnumerable<MarkupNode> Message, 
            SourceLocation NewDefinition, 
            SourceLocation OriginalDefinition)
        {
            return new MarkupNode[]
            {
                new MarkupNode("#group", Message),
                OriginalDefinition.CreateDiagnosticsNode(),
                NewDefinition.CreateRemarkDiagnosticsNode("previous declaration: ")
            };
        }

        /// <summary>
        /// Checks that the given node is an id node.
        /// </summary>
        public static bool CheckId(LNode Node, ICompilerLog Log)
        {
            if (!Node.IsId)
            {
                Log.LogError(new LogEntry(
                    "unexpected node type", 
                    HighlightEven(
                        "syntax node '", Node.Name.Name, "' was not an ", 
                        "identifier", " node, expected an identifier node."),
                    ToSourceLocation(Node.Range)));
                return false;
            }
            else
            {
                return true;
            }
        }

        /// <summary>
        /// Checks that the given node is a call node.
        /// </summary>
        public static bool CheckCall(LNode Node, ICompilerLog Log)
        {
            if (!Node.IsCall)
            {
                Log.LogError(new LogEntry(
                    "unexpected node type", 
                    HighlightEven(
                        "syntax node '", Node.Name.Name, "' was not a ", 
                        "call", " node, expected a call node."),
                    ToSourceLocation(Node.Range)));
                return false;
            }
            else
            {
                return true;
            }
        }

        /// <summary>
        /// Checks the given node's arity.
        /// </summary>
        public static bool CheckArity(LNode Node, int Arity, ICompilerLog Log)
        {
            if (Node.ArgCount != Arity)
            {
                Log.LogError(new LogEntry(
                    "unexpected node arity", 
                    HighlightEven(
                        "syntax node '", Node.Name.Name, "' had an argument count of '", 
                        Node.ArgCount.ToString(), "', expected: '", Arity.ToString(), "'."),
                    ToSourceLocation(Node.Range)));
                return false;
            }
            else
            {
                return true;
            }
        }

        /// <summary>
        /// Checks the given node's minimal arity.
        /// </summary>
        public static bool CheckMinArity(LNode Node, int MinArity, ICompilerLog Log)
        {
            if (Node.ArgCount < MinArity)
            {
                Log.LogError(new LogEntry(
                    "unexpected node arity", 
                    HighlightEven(
                        "syntax node '", Node.Name.Name, "' had an argument count of '", 
                        Node.ArgCount.ToString(), "'. Expected: at least '", MinArity.ToString(), "'."),
                    ToSourceLocation(Node.Range)));
                return false;
            }
            else
            {
                return true;
            }
        }

        /// <summary>
        /// Checks the given node's maximal arity.
        /// </summary>
        public static bool CheckMaxArity(LNode Node, int MaxArity, ICompilerLog Log)
        {
            if (Node.ArgCount > MaxArity)
            {
                Log.LogError(new LogEntry(
                    "unexpected node arity", 
                    HighlightEven(
                        "syntax node '", Node.Name.Name, "' had an argument count of '", 
                        Node.ArgCount.ToString(), "'. Expected: no more than '", MaxArity.ToString(), "'."),
                    ToSourceLocation(Node.Range)));
                return false;
            }
            else
            {
                return true;
            }
        }

        /// <summary>
        /// Checks that the given node has an empty attribute
        /// list.
        /// </summary>
        public static void CheckEmptyAttributes(
            LNode Node, ICompilerLog Log)
        {
            ConvertAttributes(Node, Log, attr => false);
        }

        /// <summary>
        /// Uses the given conversion delegate to convert
        /// the given sequence of attributes. Unsuccessful
        /// conversions, which are indicated by a 'false' return
        /// value on conversion, are reported as errors. 
        /// </summary>
        public static void ConvertAttributes(
            IEnumerable<LNode> Attributes, ICompilerLog Log, 
            Func<LNode, bool> Converter)
        {
            foreach (var item in Attributes)
            {
                if (!Converter(item))
                {
                    Log.LogError(new LogEntry(
                        "unexpected attribute",
                        HighlightEven(
                            "attribute node '", item.Name.ToString(), 
                            "' was unexpected here."),
                        ToSourceLocation(item.Range)));
                }
            }
        }

        /// <summary>
        /// Uses the given conversion delegate to convert
        /// all attributes belonging to the given node. Unsuccessful
        /// conversions, which are indicated by a 'false' return
        /// value on conversion, are reported as errors. 
        /// </summary>
        public static void ConvertAttributes(
            LNode Node, ICompilerLog Log, 
            Func<LNode, bool> Converter)
        {
            ConvertAttributes(Node.Attrs, Log, Converter);
        }

        public static IGenericParameter ToGenericParameter(
            LNode Node, IGenericMember Parent, GlobalScope Scope)
        {
            if (!Node.IsId)
            {
                Scope.Log.LogError(new LogEntry(
                    "syntax error",
                    "generic parameters must defined by a simple identifier.",
                    ToSourceLocation(Node.Range)));
                return null;
            }

            var name = Node.Name.Name;
            return new DescribedGenericParameter(name, Parent);
        }

        public static Tuple<SimpleName, Func<IGenericMember, IEnumerable<IGenericParameter>>> ToUnqualifiedName(
            LNode Node, GlobalScope Scope)
        {
            var name = Node.Name;
            if (name == CodeSymbols.Of)
            {
                var innerResults = ToUnqualifiedName(Node.Args[0], Scope);

                return Tuple.Create<SimpleName, Func<IGenericMember, IEnumerable<IGenericParameter>>>(
                    new SimpleName(
                        innerResults.Item1.Name, 
                        innerResults.Item1.TypeParameterCount + Node.Args.Count - 1), 
                    parent =>
                    {
                        var genParams = new List<IGenericParameter>(innerResults.Item2(parent));
                        foreach (var item in Node.Args.Slice(1))
                        {
                            var genParam = ToGenericParameter(item, parent, Scope);
                            if (genParam != null)
                                genParams.Add(genParam);
                        }
                        return genParams;
                    });
            }
            else if (!Node.IsId)
            {
                Scope.Log.LogError(new LogEntry(
                    "syntax error",
                    NodeHelpers.HighlightEven(
                        "node '", Node.ToString(), 
                        "' could not be interpreted as an unqualified name."),
                    NodeHelpers.ToSourceLocation(Node.Range)));
            }
            return Tuple.Create<SimpleName, Func<IGenericMember, IEnumerable<IGenericParameter>>>(
                new SimpleName(name.Name), _ => Enumerable.Empty<IGenericParameter>());
        }

        public static SimpleName ToSimpleName(LNode Node, GlobalScope Scope)
        {
            var qualName = ToUnqualifiedName(Node, Scope);
            if (qualName.Item1.TypeParameterCount > 0)
            {
                Scope.Log.LogError(new LogEntry(
                    "syntax error",
                    "simple names cannot have generic parameters in this context",
                    NodeHelpers.ToSourceLocation(Node.Range)));
            }
            return qualName.Item1;
        }

        /// <summary>
        /// Checks that the given node is a valid
        /// identifier node, i.e. it describes a valid
        /// name.
        /// </summary>
        public static bool CheckValidIdentifier(LNode IdNode, ICompilerLog Log)
        {
            if (!IdNode.IsId)
            {
                Log.LogError(new LogEntry(
                    "syntax error",
                    NodeHelpers.HighlightEven("expected an identifier."),
                    NodeHelpers.ToSourceLocation(IdNode.Range)));
                return false;
            }
            else if (IdNode.HasSpecialName)
            {
                Log.LogError(new LogEntry(
                    "syntax error",
                    NodeHelpers.HighlightEven("'", IdNode.Name.Name, "' is not an acceptable identifier."),
                    NodeHelpers.ToSourceLocation(IdNode.Range)));
                return false;
            }
            else
            {
                return true;
            }
        }

        /// <summary>
        /// If the given node is a valid identifier, then an (identifier, null)
        /// tuple is returned. If it is an assignment to an identifier, then
        /// that assignment will be decomposed, and an (identifier, value)
        /// tuple is returned.
        /// </summary>
        public static Tuple<LNode, LNode> DecomposeAssignOrId(LNode AssignOrId, ICompilerLog Log)
        {
            if (AssignOrId.Calls(CodeSymbols.Assign))
            {
                if (!NodeHelpers.CheckArity(AssignOrId, 2, Log)
                    || !CheckValidIdentifier(AssignOrId.Args[0], Log))
                    return null;
                else
                    return Tuple.Create(AssignOrId.Args[0], AssignOrId.Args[1]);
            }
            else if (AssignOrId.IsId)
            {
                if (!CheckValidIdentifier(AssignOrId, Log))
                    return null;
                else
                    return Tuple.Create<LNode, LNode>(AssignOrId, null);
            }
            else
            {
                Log.LogError(new LogEntry(
                    "syntax error",
                    "expected an identifier or an assignment to an identifier.",
                    NodeHelpers.ToSourceLocation(AssignOrId.Range)));
                return null;
            }
        }

        /// <summary>
        /// Partition the specified sequence based on 
        /// the given predicate.
        /// </summary>
        public static Tuple<IEnumerable<T>, IEnumerable<T>> Partition<T>(
            IEnumerable<T> Sequence, Func<T, bool> Predicate)
        {
            var first = new List<T>();
            var second = new List<T>();
            foreach (var item in Sequence)
            {
                if (Predicate(item))
                    first.Add(item);
                else
                    second.Add(item);
            }
            return Tuple.Create<IEnumerable<T>, IEnumerable<T>>(first, second);
        }

        /// <summary>
        /// Determines if the given symbol is an access modifier attribute.
        /// </summary>
        public static bool IsAccessModifier(Symbol S)
        {
            return accessModifiers.Contains(S);
        }

        /// <summary>
        /// Tries to find an access modifier that matches the given
        /// set of access modifier symbols.
        /// </summary>
        public static AccessModifier? ToAccessModifier(HashSet<Symbol> Symbols)
        {
            AccessModifier result;
            if (accModMap.TryGetValue(Symbols, out result))
                return result;
            else
                return null;
        }

        private static readonly HashSet<Symbol> accessModifiers = new HashSet<Symbol>()
        {
            CodeSymbols.Private, CodeSymbols.Protected,
            CodeSymbols.Internal, CodeSymbols.Public,
            CodeSymbols.ProtectedIn, CodeSymbols.ProtectedIn,
            CodeSymbols.FilePrivate
        };

        private static readonly IReadOnlyDictionary<HashSet<Symbol>, AccessModifier> accModMap = new Dictionary<HashSet<Symbol>, AccessModifier>(HashSet<Symbol>.CreateSetComparer())
        {
            { new HashSet<Symbol>() { CodeSymbols.Private }, AccessModifier.Private },
            { new HashSet<Symbol>() { CodeSymbols.Protected }, AccessModifier.Protected },
            { new HashSet<Symbol>() { CodeSymbols.Internal }, AccessModifier.Assembly },
            { new HashSet<Symbol>() { CodeSymbols.Public }, AccessModifier.Public },
            { new HashSet<Symbol>() { CodeSymbols.Protected, CodeSymbols.Internal }, AccessModifier.ProtectedOrAssembly },
        };

        /// <summary>
        /// Determines if the given attribute is the 'static' attribute.
        /// </summary>
        /// <returns><c>true</c> if the given attribute is the 'static' attribute; otherwise, <c>false</c>.</returns>
        public static bool IsStaticAttribute(LNode Attribute)
        {
            return Attribute.IsIdNamed(CodeSymbols.Static);
        }

        /// <summary>
        /// Determines if any of the given attribute are the 'static' attribute.
        /// </summary>
        /// <returns><c>true</c> if any of the given attribute are the 'static' attribute; otherwise, <c>false</c>.</returns>
        public static bool ContainsStaticAttribute(IEnumerable<LNode> Attibutes)
        {
            return Attibutes.Any(IsStaticAttribute);
        }

        /// <summary>
        /// Determines if the given node is an extension method.
        /// </summary>
        /// <remarks>
        /// This function can be used to detect if a class 
        /// contains extension methods before attempting to
        /// analyze the members.
        /// </remarks>
        public static bool IsExtensionMethod(LNode Node)
        {
            if (!Node.CallsMin(CodeSymbols.Fn, 3))
                return false;

            var paramList = Node.Args[2].Args;
            return paramList.Any(item => 
                item.Attrs.Any(attr => 
                    attr.IsIdNamed(CodeSymbols.This)));
        }
    }
}

