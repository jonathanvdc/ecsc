
using System.Collections.Generic;
using System.Linq;
using Flame.Compiler;
using Flame.Compiler.Expressions;
using Flame.Compiler.Statements;
using Flame.Ecs.Values;
using Loyc.Syntax;
using Pixie;

namespace Flame.Ecs
{
    /// <summary>
    /// A data structure that helps build switch statements.
    /// </summary>
    public sealed class SwitchBuilder
    {
        public SwitchBuilder(
            IValue SwitchValue, SourceLocation SwitchValueLocation,
            LocalScope Scope, NodeConverter Converter)
        {
            this.SwitchValue = SwitchValue;
            this.SwitchValueLocation = SwitchValueLocation;
            this.Scope = Scope;
            this.Converter = Converter;
            this.defaultCase = null;
            this.otherCases = new List<KeyValuePair<IExpression, IStatement>>();
            this.isBuildingDefaultCase = false;
            this.caseConditions = new List<IExpression>();
            this.caseStatements = new List<IStatement>();
            this.caseValues = new Dictionary<object, SourceLocation>();
        }

        /// <summary>
        /// Gets the value that this switch statement operates on.
        /// </summary>
        /// <returns>The value that this switch statement operates on.</returns>
        public IValue SwitchValue { get; private set; }

        /// <summary>
        /// Gets the location of the value that this switch statement operates on.
        /// </summary>
        /// <returns>The location of the value that this switch statement operates on.
        public SourceLocation SwitchValueLocation { get; private set; }

        /// <summary>
        /// Gets this switch builder's local scope.
        /// </summary>
        /// <returns>The local scope for this switch builder.</returns>
        public LocalScope Scope { get; private set; }

        /// <summary>
        /// Gets this switch builder's node converter.
        /// </summary>
        /// <returns>The node converter for this switch builder.</returns>
        public NodeConverter Converter { get; private set; }

        private IStatement defaultCase;
        private SourceLocation defaultCaseLocation;

        private List<KeyValuePair<IExpression, IStatement>> otherCases;

        /// <summary>
        /// Tells if this switch builder already has a default case.
        /// </summary>
        public bool HasDefaultCase => defaultCaseLocation != null;

        private bool isBuildingDefaultCase;

        private List<IExpression> caseConditions;

        private List<IStatement> caseStatements;

        private Dictionary<object, SourceLocation> caseValues;

        public void AppendCondition(LNode CaseValueNode)
        {
            if (caseStatements.Count > 0)
                FlushCase();

            var caseExpr = Converter.ConvertExpression(
                CaseValueNode, Scope, SwitchValue.Type);
            var caseLoc = NodeHelpers.ToSourceLocation(
                CaseValueNode.Range);

            var caseEval = caseExpr.EvaluateOrNull();
            if (caseEval == null)
            {
                Scope.Log.LogError(
                    new LogEntry(
                        "cannot evaluate",
                        NodeHelpers.HighlightEven(
                            "'", "case", "' value is not a compile-time constant."),
                        caseLoc));
            }
            else
            {
                var caseObj = caseEval.GetObjectValue();
                SourceLocation prevLoc;
                if (caseValues.TryGetValue(caseObj, out prevLoc))
                {
                    Scope.Log.LogError(
                        new LogEntry(
                            "duplicate cases",
                            NodeHelpers.HighlightEven(
                                "same '", "case",
                                "' appears more than once in the same '",
                                "switch", "' statement.").Concat(new MarkupNode[]
                                {
                                    caseLoc.CreateDiagnosticsNode(),
                                    prevLoc.CreateRemarkDiagnosticsNode("duplicate case: ")
                                })));
                    return;
                }
                else
                {
                    caseValues[caseObj] = caseLoc;
                }
            }

            caseConditions.Add(
                Scope.Function.ConvertImplicit(
                    ExpressionConverters.CreateBinary(
                        Operator.CheckEquality,
                        SwitchValue,
                        new ExpressionValue(caseExpr),
                        Scope.Function,
                        SwitchValueLocation,
                        caseLoc),
                    PrimitiveTypes.Boolean,
                    caseLoc));
        }

        public void AppendDefault(LNode DefaultNode)
        {
            var defaultLoc = NodeHelpers.ToSourceLocation(DefaultNode.Range);
            if (HasDefaultCase)
            {
                Scope.Log.LogError(
                    new LogEntry(
                        "syntax error",
                        NodeHelpers.HighlightEven(
                            "more than one '", "default", "' case in a single '",
                            "switch", "' statement.").Concat(new MarkupNode[]
                            {
                                defaultLoc.CreateDiagnosticsNode(),
                                defaultCaseLocation.CreateRemarkDiagnosticsNode("other default case: ")
                            })));
                return;
            }

            if (caseStatements.Count > 0)
                FlushCase();

            isBuildingDefaultCase = true;
            defaultCaseLocation = defaultLoc;
        }

        public void AppendStatement(LNode StatementNode)
        {
            if (!isBuildingDefaultCase && caseConditions.Count == 0)
            {
                Scope.Log.LogError(
                    new LogEntry(
                        "syntax error",
                        NodeHelpers.HighlightEven(
                            "child statements in a '", "switch",
                            "' statement must be preceded by a valid '",
                            "case", "' or '", "default", "' label.").Concat(new MarkupNode[]
                            {
                                NodeHelpers.ToSourceLocation(StatementNode.Range).CreateDiagnosticsNode(),
                                defaultCaseLocation.CreateRemarkDiagnosticsNode("other default case: ")
                            })));
            }

            caseStatements.Add(Converter.ConvertStatement(StatementNode, Scope));
        }

        public IStatement FinishSwitch()
        {
            FlushCase();

            var switchStmt = defaultCase;
            for (int i = otherCases.Count - 1; i >= 0; i--)
            {
                switchStmt = new IfElseStatement(
                    otherCases[i].Key, otherCases[i].Value, switchStmt);
            }
            return switchStmt;
        }

        private void FlushCase()
        {
            if (!isBuildingDefaultCase && caseConditions.Count == 0)
                return;

            var caseBody = new BlockStatement(caseStatements).Simplify();
            if (isBuildingDefaultCase)
            {
                defaultCase = caseBody;
                isBuildingDefaultCase = false;
            }
            else
            {
                var condition = caseConditions[0];
                for (int i = 1; i < caseConditions.Count; i++)
                {
                    condition = new LazyOrExpression(condition, caseConditions[i]);
                }

                otherCases.Add(new KeyValuePair<IExpression, IStatement>(condition, caseBody));
            }

            caseConditions = new List<IExpression>();
            caseStatements = new List<IStatement>();
        }
    }
}