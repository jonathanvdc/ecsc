using System;
using Flame.Compiler;
using System.Threading;

namespace Flame.Ecs
{
    public class LazyDescribedField : LazyDescribedTypeMember, IInitializedField
    {
        public LazyDescribedField(string Name, IType DeclaringType, Action<LazyDescribedField> AnalyzeBody)
            : base(Name, DeclaringType)
        {
            this.analyzeBody = AnalyzeBody;
        }

        private Action<LazyDescribedField> analyzeBody;

        private IType fieldTy;

        public IType FieldType
        { 
            get
            {
                CreateBody();
                return fieldTy;
            }
            set
            { 
                CreateBody();
                fieldTy = value;
            }
        }

        private bool isStaticVal;
        public override bool IsStatic
        {
            get
            {
                CreateBody();
                return isStaticVal;
            }
            set
            {
                CreateBody();
                isStaticVal = value;
            }
        }

        private IExpression bodyExpr;

        public IExpression Value
        { 
            get
            {
                CreateBody();
                return bodyExpr;
            }
            set
            { 
                CreateBody();
                bodyExpr = value; 
            }
        }

        public IExpression GetValue()
        {
            return Value;
        }

        protected override void CreateBody()
        {
            var f = Interlocked.CompareExchange(
                ref analyzeBody, null, analyzeBody);
            if (f != null)
            {
                f(this);
            }
        }
    }
}

