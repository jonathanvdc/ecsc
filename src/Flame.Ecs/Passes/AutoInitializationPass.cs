using Flame.Analysis;
using Flame.Compiler;
using Flame.Compiler.Build;
using Flame.Compiler.Expressions;
using Flame.Compiler.Statements;
using Flame.Compiler.Variables;
using Flame.Compiler.Visitors;
using Flame.Syntax;
using Flame.Verification;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Flame.Ecs.Passes
{
    /// <summary>
    /// A pass that inserts initialization nodes if initialization cannot be proven.
    /// </summary>
    public class AutoInitializationPass : IPass<Tuple<IStatement, IMethod, ICompilerLog>, IStatement>
    {
        private AutoInitializationPass()
        {
        }

        public static readonly AutoInitializationPass Instance = new AutoInitializationPass();

        public static readonly WarningDescription StructAutoInitializationWarning =
            new WarningDescription("struct-auto-initialization", Warnings.Instance.Pedantic);

        public const string AutoInitializationPassName = "auto-initialization";

        private static IStatement CreateAutoInitializationStatement(ICompilerLog Log, IMethod DeclaringMethod, IType DeclaringType, NodeCountVisitor Visitor)
        {
            var thisVariable = ThisReferenceVariable.Instance.Create(DeclaringType);
            if (DeclaringType.GetIsValueType())
            {
                if (StructAutoInitializationWarning.UseWarning(Log.Options) && DeclaringType.GetIsValueType())
                {
                    var entry = new LogEntry(
                        "Value type auto-initialization",
                        StructAutoInitializationWarning.CreateMessage(
                            "A value type's constructed instance ('this') was auto-initialized, because the constructor did not manually initialize it" +
                            (Visitor.CurrentFlow.Max.Value == 0 ? ". " : " in every control flow path. ") +
                            "Consider rewriting the constructor to initialize the constructed instance with a 'this = default(...);' statement. "),
                        DeclaringMethod.GetSourceLocation());
                    Log.LogWarning(entry);
                }

                return thisVariable.CreateSetStatement(new DefaultValueExpression(thisVariable.Type));
            }
            else
            {
                var baseType = thisVariable.Type.GetParent();
                var parameterlessCtor = baseType.GetConstructor(new IType[] { }, false);
                if (parameterlessCtor == null)
                {
                    var entry = new LogEntry(
                        "missing parameterless constructor",
                        (Visitor.CurrentFlow.Max == 0 ?
                            "a constructor that did not manually initialize the constructed instance " :
                            "A constructor that contains some control flow paths that may not manually initialize the constructed instance ") +
                            "could not auto-initialize said instance, because the base type does not have a parameterless constructor.",
                            DeclaringMethod.GetSourceLocation());
                    entry = new LogEntry(entry.Name, RedefinitionHelpers.Instance.AppendDiagnosticsRemark(entry.Contents, "Base type: ", baseType.GetSourceLocation()));
                    Log.LogError(InitializationCountPass.AppendInitializationLocations(entry, Visitor));
                    return EmptyStatement.Instance;
                }
                else
                {
                    return new ExpressionStatement(
                        new InvocationExpression(
                            parameterlessCtor,
                            new ReinterpretCastExpression(
                                thisVariable.CreateGetExpression(),
                                baseType),
                        new IExpression[] { }));
                }
            }
        }

        /// <summary>
        /// Checks if the given visitor's current flow indicates possibly uninitialized flow,
        /// and adds an auto-initialization node if that is the case. A default warning is shown
        /// if auto-initialization may result in multiple initialization, and a pedantic warning is shown
        /// if a value type is auto-initialized.
        /// A boolean is returned that tells if the control flow was auto-initialized.
        /// </summary>
        /// <param name="Log"></param>
        /// <param name="Target"></param>
        /// <param name="Visitor"></param>
        /// <returns></returns>
        public static bool AutoInitialize(ICompilerLog Log, IMethod Target, NodeCountVisitor Visitor, ref IStatement Body)
        {
            if (Visitor.CurrentFlow.Min == 0)
            {
                var declType = Target.DeclaringType;

                var autoInitStmt = CreateAutoInitializationStatement(Log, Target, declType, Visitor);

                if (Visitor.CurrentFlow.Max > 0 && InitializationCountPass.MaybeMultipleInitializationWarning.UseWarning(Log.Options))
                {
                    var msg = new LogEntry(
                        "instance possibly initialized more than once",
                        InitializationCountPass.MaybeMultipleInitializationWarning.CreateMessage(
                            "the constructed instance may be initialized more than once in some control flow paths, " +
                            "because an auto-initialization statement was inserted. "),
                        Target.GetSourceLocation());
                    Log.LogWarning(InitializationCountPass.AppendInitializationLocations(msg, Visitor));
                }

                Body = new BlockStatement(new IStatement[] { autoInitStmt, Body });

                return true;
            }
            else
            {
                return false;
            }
        }

        public IStatement Apply(Tuple<IStatement, IMethod, ICompilerLog> Value)
        {
            var method = Value.Item2;
            if (method.IsConstructor && !method.IsStatic
                && (method.DeclaringType.GetIsValueType() || method.DeclaringType.GetParent() != null))
            {
                var visitor = InitializationCountHelpers.CreateVisitor();
                var body = Value.Item1;
                visitor.Visit(body);
                if (!AutoInitialize(Value.Item3, method, visitor, ref body))
                {
                    InitializationCountPass.LogMultipleInitialization(Value.Item3, method, visitor);
                }
                return body;
            }
            else
            {
                return Value.Item1;
            }
        }
    }
}
