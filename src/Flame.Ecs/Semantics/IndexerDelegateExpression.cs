using System;
using System.Collections.Generic;
using System.Linq;
using Flame.Compiler;
using Flame.Compiler.Expressions;
using Flame.Compiler.Variables;

namespace Flame.Ecs
{
    /// <summary>
    /// Defines a delegate expression for indexers.
    /// </summary>
    public class IndexerDelegateExpression : IDelegateExpression, IMemberNode
    {
        public IndexerDelegateExpression(IProperty Property, IExpression Target)
        {
            this.Property = Property;
            this.Target = Target;
        }

        /// <summary>
        /// Gets the accessor that this indexer delegate 
        /// expression uses.
        /// </summary>
        public IProperty Property { get; private set; }

        /// <summary>
        /// Gets the target expression for this indexer
        /// delegate expression.
        /// </summary>
        public IExpression Target { get; private set; }

        public IEnumerable<IType> ParameterTypes
        { 
            get { return Property.IndexerParameters.GetTypes(); } 
        }

        public IType ReturnType
        {
            get { return Property.PropertyType; }
        }

        public bool IsConstantNode
        {
            get { return true; }
        }

        public IType Type
        {
            get
            {
                // TODO: Maybe get this right, even when there's no
                // get-accessor?
                return MethodType.Create(Property.GetGetAccessor()); 
            }
        }

        public IBoundObject Evaluate()
        {
            return null;
        }

        public IExpression Optimize()
        {
            return new IndexerDelegateExpression(Property, Target.Optimize());
        }

        public IExpression Accept(INodeVisitor Visitor)
        {
            return this;
        }

        public ICodeBlock Emit(ICodeGenerator Generator)
        {
            return new GetMethodExpression(Property.GetGetAccessor(), Target).Emit(Generator);
        }

        public IMemberNode ConvertMembers(MemberConverter Converter)
        {
            var acc = Property.Accessors.FirstOrDefault();

            return new IndexerDelegateExpression(
                acc == null ? Property : ((IAccessor)Converter.Convert(acc)).DeclaringProperty,
                Target);
        }

        public IExpression CreateInvocationExpression(IEnumerable<IExpression> Arguments)
        {
            return new AccessIndexerExpression(Property, Target, Arguments);
        }

        public IDelegateExpression MakeGenericExpression(IEnumerable<IType> TypeArguments)
        {
            throw new InvalidOperationException("indexer-delegate expressions cannot be instantiated with type arguments.");
        }
    }

    /// <summary>
    /// An expression that accesses an indexer.
    /// </summary>
    public class AccessIndexerExpression : ComplexExpressionBase
    {
        public AccessIndexerExpression(IProperty Property, IExpression Target, IEnumerable<IExpression> Arguments)
        {
            this.Property = Property;
            this.Target = Target;
            this.Arguments = Arguments;
        }

        /// <summary>
        /// Gets the accessor that this indexer-access expression uses.
        /// </summary>
        public IProperty Property { get; private set; }

        /// <summary>
        /// Gets the target expression for this indexer-access expression.
        /// </summary>
        public IExpression Target { get; private set; }

        /// <summary>
        /// Gets the argument list for this indexer-access expression.
        /// </summary>
        public IEnumerable<IExpression> Arguments { get; private set; }

        /// <inheritdoc/>
        public override IType Type
        {
            get
            {
                return Property.PropertyType;
            }
        }

        /// <inheritdoc/>
        protected override IExpression Lower()
        {
            return new PropertyVariable(Property, Target, Arguments).CreateGetExpression();
        }
    }
}

