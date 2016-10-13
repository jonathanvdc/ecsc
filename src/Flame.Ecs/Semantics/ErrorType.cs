using System;
using System.Collections.Generic;
using System.Linq;

namespace Flame.Ecs
{
    /// <summary>
    /// A special type that is used when an error occurs. The error
    /// type is convertible to any type, and vice-versa. This property
    /// is useful for reducing the number of indirect errors: errors
    /// that have other errors as their root causes.
    /// </summary>
    public class ErrorType : IType
    {
        private ErrorType()
        { }

        public static readonly ErrorType Instance = new ErrorType();

        public UnqualifiedName Name { get; private set; } = new SimpleName("?");
        public QualifiedName FullName { get { return Name.Qualify(); } }

        public AttributeMap Attributes
        {
            get { return AttributeMap.Empty; }
        }

        public IEnumerable<IGenericParameter> GenericParameters
        {
            get { return Enumerable.Empty<IGenericParameter>(); }
        }

        public IAncestryRules AncestryRules
        {
            get { return ErrorTypeAncestryRules.Instance; }
        }

        public IEnumerable<IType> BaseTypes
        {
            get { return Enumerable.Empty<IType>(); }
        }

        public INamespace DeclaringNamespace
        {
            get { return null; }
        }

        public IEnumerable<IField> Fields
        {
            get { return Enumerable.Empty<IField>(); }
        }

        public IEnumerable<IMethod> Methods
        {
            get { return Enumerable.Empty<IMethod>(); }
        }

        public IEnumerable<IProperty> Properties
        {
            get { return Enumerable.Empty<IProperty>(); }
        }

        public IBoundObject GetDefaultValue()
        {
            return null;
        }
    }

    /// <summary>
    /// A set of ancestry rules for error types. The rules
    /// stipulate that the error type is any other type, and
    /// any other type is the error type.
    /// </summary>
    public class ErrorTypeAncestryRules : IAncestryRules
    {
        private ErrorTypeAncestryRules()
        { }

        public static readonly ErrorTypeAncestryRules Instance = new ErrorTypeAncestryRules();

        public int GetAncestryDegree(IType First, IType Second)
        {
            if (First is ErrorType || Second is ErrorType)
            {
                return object.Equals(First, Second)
                    ? 0
                    : 1;
            }
            else
            {
                return -1;
            }
        }
    }
}

