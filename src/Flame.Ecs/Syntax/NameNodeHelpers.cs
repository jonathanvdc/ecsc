using System;
using System.Linq;
using Loyc.Syntax;
using Flame.Compiler;

namespace Flame.Ecs.Syntax
{
    public static class NameNodeHelpers
    {
        /// <summary>
        /// Converts the given identifier node to a simple name,
        /// without any generic parameters.
        /// </summary>
        /// <returns>The simple name.</returns>
        /// <param name="Node">The syntax node to operate on.</param>
        /// <param name="Scope">The scope which is used to log errors.</param>
        public static SimpleName ToSimpleIdentifier(LNode Node, GlobalScope Scope)
        {
            if (!Node.IsId)
            {
                Scope.Log.LogError(new LogEntry(
                    "syntax error",
                    NodeHelpers.HighlightEven(
                        "node '", Node.ToString(), 
                        "' is not a simple identifier."),
                    NodeHelpers.ToSourceLocation(Node.Range)));
            }
            return new SimpleName(Node.Name.Name);
        }

        /// <summary>
        /// Converts the given syntax node to a generic constraint node.
        /// </summary>
        /// <returns>The generic constraint.</returns>
        /// <param name="Node">The syntax node to operate on.</param>
        public static IGenericConstraintNode ToGenericConstraintNode(LNode Node)
        {
            if (Node.IsIdNamed(CodeSymbols.Class))
            {
                return ClassConstraintNode.Instance;
            }
            else if (Node.IsIdNamed(CodeSymbols.Struct))
            {
                return StructConstraintNode.Instance;
            }
            else if (Node.IsIdNamed(CodeSymbols.Enum))
            {
                return EnumConstraintNode.Instance;
            }
            else
            {
                return new TypeConstraintNode(Node);
            }
        }

        /// <summary>
        /// Converts the given syntax node to a generic parameter 
        /// definition data structure.
        /// </summary>
        /// <returns>The generic parameter definition.</returns>
        /// <param name="Node">The syntax node to operate on.</param>
        /// <param name="Scope">The scope which is used to log errors.</param>
        public static GenericParameterDef ToGenericParameterDef(LNode Node, GlobalScope Scope)
        {
            var name = ToSimpleIdentifier(Node, Scope);
            var constraints = Node.Attrs
                .Where(n => n.Calls(CodeSymbols.Where))
                .SelectMany(n => n.Args)
                .Select(ToGenericConstraintNode)
                .ToArray();
            return new GenericParameterDef(name, constraints);
        }

        /// <summary>
        /// Converts the given node to a generic member name.
        /// </summary>
        /// <returns>The generic member name.</returns>
        /// <param name="Node">The syntax node to operate on.</param>
        /// <param name="Scope">The scope which is used to log errors.</param>
        public static GenericMemberName ToGenericMemberName(LNode Node, GlobalScope Scope)
        {
            if (Node.Calls(CodeSymbols.Of))
            {
                var name = ToSimpleIdentifier(Node.Args[0], Scope);
                var genParams = Node.Args.Slice(1)
                    .Select(n => ToGenericParameterDef(n, Scope))
                    .ToArray();
                return new GenericMemberName(
                    new SimpleName(name.Name, genParams.Length), 
                    genParams);
            }
            else
            {
                return new GenericMemberName(
                    ToSimpleIdentifier(Node, Scope),
                    new GenericParameterDef[] { });
            }
        }
    }
}

