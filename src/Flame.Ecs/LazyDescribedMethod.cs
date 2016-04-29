﻿using System;
using Flame.Build;
using System.Collections.Generic;
using Flame.Compiler;
using System.Threading;

namespace Flame.Ecs
{
    public abstract class LazyDescribedMember : IMember
    {
        public LazyDescribedMember(string Name)
        {
            this.Name = Name;
            this.attributeList = new List<IAttribute>();
        }

        public string Name { get; private set; }
        public abstract string FullName { get; }

        protected abstract void CreateBody();

        private List<IAttribute> attributeList;

        public void AddAttribute(IAttribute Attribute)
        {
            CreateBody();
            attributeList.Add(Attribute);
        }

        public IEnumerable<IAttribute> Attributes
        {
            get 
            { 
                CreateBody();
                return attributeList; 
            }
        }
    }

    public abstract class LazyDescribedTypeMember : LazyDescribedMember, ITypeMember
	{
		public LazyDescribedTypeMember(string Name, IType DeclaringType)
			: base(Name)
		{
			this.DeclaringType = DeclaringType;
		}

		/// <summary>
		/// Gets the type that declared this member.
		/// </summary>
		/// <value>The type of the declaring.</value>
		public IType DeclaringType { get; private set; }

		public sealed override string FullName
		{
			get
			{
				if (this.DeclaringType == null)
				{
					return base.Name;
				}
				return MemberExtensions.CombineNames(this.DeclaringType.FullName, base.Name);
			}
		}

		public abstract bool IsStatic { get; set; }

		public override string ToString()
		{
			return this.FullName;
		}
	}

	public class LazyDescribedMethod : LazyDescribedTypeMember, IBodyMethod
	{
		public LazyDescribedMethod(string Name, IType DeclaringType, Action<LazyDescribedMethod> AnalyzeBody)
			: base(Name, DeclaringType)
		{
			this.baseMethods = new List<IMethod>();
			this.analyzeBody = AnalyzeBody;
		}

		private Action<LazyDescribedMethod> analyzeBody;

		private IType retType;

		public IType ReturnType
		{ 
			get
			{
				CreateBody();
				return retType;
			}
			set
			{ 
				CreateBody();
				retType = value;
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

		private bool isCtorVal;

		public bool IsConstructor
		{
			get
			{
				CreateBody();
				return isCtorVal;
			}
			set
			{
				CreateBody();
				isCtorVal = value;
			}
		}

		private IStatement bodyStmt;

		public IStatement Body
		{ 
			get
			{
				CreateBody();
				return bodyStmt;
			}
			set
			{ 
				CreateBody();
				bodyStmt = value; 
			}
		}

		/// <summary>
		/// Gets the method's body statement.
		/// </summary>
		/// <returns></returns>
		public IStatement GetMethodBody()
		{
			CreateBody();
			return Body;
		}

		protected override void CreateBody()
		{
            var f = Interlocked.CompareExchange(
                ref analyzeBody, null, analyzeBody);
            if (f != null)
            {
				this.parameters = new List<IParameter>();
				this.baseMethods = new List<IMethod>();
				this.genericParams = new List<IGenericParameter>();

				f(this);
			}
		}

		private List<IParameter> parameters;

		public virtual void AddParameter(IParameter Parameter)
		{
			parameters.Add(Parameter);
		}

		public IEnumerable<IParameter> Parameters
		{
			get
			{
				CreateBody();
				return parameters;
			}
		}

		private List<IMethod> baseMethods;

		public virtual void AddBaseMethod(IMethod Method)
		{
			baseMethods.Add(Method);
		}

		public IEnumerable<IMethod> BaseMethods
		{
			get
			{
				CreateBody();
				return baseMethods.ToArray();
			}
		}

		private List<IGenericParameter> genericParams;

		public virtual void AddGenericParameter(IGenericParameter Parameter)
		{
			genericParams.Add(Parameter);
		}

		/// <summary>
		/// Gets this method's generic parameters.
		/// </summary>
		/// <returns></returns>
		public IEnumerable<IGenericParameter> GenericParameters
		{
			get
			{
				CreateBody();
				return genericParams;
			}
		}
	}
}
