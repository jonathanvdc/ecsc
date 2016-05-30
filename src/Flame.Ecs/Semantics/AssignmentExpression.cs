using System;
using Flame.Compiler;
using Flame.Compiler.Expressions;
using Flame.Compiler.Statements;
using Flame.Compiler.Variables;
using Flame.Optimization;

namespace Flame.Ecs.Semantics
{
    /// <summary>
    /// An assignment variable: an expression that corresponds 
    /// to a store to another variable, which may then
    /// be used again in another store.
    /// </summary>
    public class AssignmentExpression : ComplexExpressionBase
    {
        public AssignmentExpression(
            IVariable TargetVariable, IExpression Value)
        {
            this.tempVar = new RegisterVariable("tmp", Value.Type);
            this.storeValExpr = new SpeculativeExpression(
                Value, tempVar.CreateGetExpression());
            this.storeStmt = TargetVariable.CreateSetStatement(storeValExpr);
        }

        private SpeculativeExpression storeValExpr;
        private IStatement storeStmt;
        private IVariable tempVar;

        public override IType Type
        {
            get
            {
                return tempVar.Type;
            }
        }

        public override bool IsConstantNode
        {
            get
            {
                return false;
            }
        }

        public IStatement ToStatement()
        {
            return storeStmt;
        }

        protected override IExpression Lower()
        {
            // Revert to conservative behavior.
            storeValExpr.IsAlive = false;
            return new InitializedExpression(
                new BlockStatement(new IStatement[]
                {
                    tempVar.CreateSetStatement(storeValExpr.LiveContents),
                    storeStmt
                }),
                tempVar.CreateGetExpression(),
                tempVar.CreateReleaseStatement());
        }
    }
}

