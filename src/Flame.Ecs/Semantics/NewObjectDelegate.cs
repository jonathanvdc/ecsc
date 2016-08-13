using System;
using System.Collections.Generic;
using Flame.Compiler;
using Flame.Compiler.Expressions;

namespace Flame.Ecs
{
    /// <summary>
    /// Defines a delegate expression for object creation.
    /// These expressions must always be invoked: they
    /// cannot be used in any other context.
    /// </summary>
    public class NewObjectDelegate : IDelegateExpression, IMemberNode
    {
        public NewObjectDelegate(IMethod Constructor)
        {
            this.Constructor = Constructor;
        }

        /// <summary>
        /// Gets the constructor that this new-object delegate uses.
        /// </summary>
        public IMethod Constructor { get; private set; }

        public IEnumerable<IType> ParameterTypes
        { 
            get { return Constructor.Parameters.GetTypes(); } 
        }

        public IType ReturnType
        {
            get { return Constructor.DeclaringType; }
        }

        public bool IsConstantNode
        {
            get { return true; }
        }

        public IType Type
        {
            get
            {
                // Note: perhaps we should create a method type
                // with a return value to match this delegate's
                // return type. 
                // That would require some changes to the
                // overload resolution diagnostics, though.
                return MethodType.Create(Constructor); 
            }
        }

        public IBoundObject Evaluate()
        {
            return null;
        }

        public IExpression Optimize()
        {
            return this;
        }

        public IExpression Accept(INodeVisitor Visitor)
        {
            return this;
        }

        public ICodeBlock Emit(ICodeGenerator Generator)
        {
            throw new NotImplementedException(
                "new-object delegates cannot exist as stand-alone expressions right now.");
        }

        public IMemberNode ConvertMembers(MemberConverter Converter)
        {
            return new NewObjectDelegate(Converter.Convert(Constructor));
        }

        public IExpression CreateInvocationExpression(IEnumerable<IExpression> Arguments)
        {
            return new NewObjectExpression(Constructor, Arguments);
        }

        public IDelegateExpression MakeGenericExpression(IEnumerable<IType> TypeArguments)
        {
            return new NewObjectDelegate(Constructor.MakeGenericMethod(TypeArguments));
        }
    }
}

