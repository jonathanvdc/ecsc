using System;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace Flame.Ecs
{
	/// <summary>
	/// A type of object that maps qualified names to types.
	/// </summary>
	public sealed class QualifiedBinder
	{
		private QualifiedBinder(
			IBinder Binder, List<QualifiedName> namespaceUsings, 
			Dictionary<string, QualifiedName> nameAliases,
			Dictionary<string, IType> typeAliases)
		{
			this.Binder = Binder;
			this.namespaceUsings = namespaceUsings;
			this.nameAliases = nameAliases;
			this.typeAliases = typeAliases;
			this.resolvedTypeCache = new ConcurrentDictionary<QualifiedName, IType>();
		}
		public QualifiedBinder(IBinder Binder)
			: this(
				Binder, new List<QualifiedName>(), 
				new Dictionary<string, QualifiedName>(),
				new Dictionary<string, IType>())
		{ }

		/// <summary>
		/// Gets this qualified name binder's underlying binder.
		/// </summary>
		public IBinder Binder { get; private set; }

		private List<QualifiedName> namespaceUsings;
		private Dictionary<string, QualifiedName> nameAliases;
		private Dictionary<string, IType> typeAliases;
		private ConcurrentDictionary<QualifiedName, IType> resolvedTypeCache;

		/// <summary>
		/// Adds the given qualified name to the list of used namespaces.
		/// </summary>
		public QualifiedBinder UseNamespace(QualifiedName Name)
		{
			var newUsings = new List<QualifiedName>(namespaceUsings);
			newUsings.Add(Name);
			return new QualifiedBinder(Binder, newUsings, nameAliases, typeAliases);
		}

		/// <summary>
		/// Creates an alias for a qualified name.
		/// </summary>
		public QualifiedBinder AliasName(string Alias, QualifiedName Name)
		{
			var newAliases = new Dictionary<string, QualifiedName>(nameAliases);
			newAliases[Alias] = Name;
			return new QualifiedBinder(Binder, namespaceUsings, newAliases, typeAliases);
		}

		/// <summary>
		/// Creates an alias for the given type.
		/// </summary>
		public QualifiedBinder AliasType(string Alias, IType Type)
		{
			var tyAliases = new Dictionary<string, IType>(typeAliases);
			tyAliases[Alias] = Type;
			return new QualifiedBinder(Binder, namespaceUsings, nameAliases, tyAliases);
		}

		/// <summary>
		/// Binds the given qualified name to a type.
		/// </summary>
		public IType BindType(QualifiedName Name)
		{
			return resolvedTypeCache.GetOrAdd(Name, BindTypeImpl);
		}

		private IType BindTypeImpl(QualifiedName Name)
		{
			if (Name.IsEmpty)
				return null;

			IType result;
			if (!Name.IsQualified && typeAliases.TryGetValue(Name.Qualifier, out result))
				return result;

			QualifiedName aliasedQualifier;
			if (nameAliases.TryGetValue(Name.Qualifier, out aliasedQualifier))
				Name = Name.Qualify(aliasedQualifier);

			result = Binder.BindType(Name.FullName);
			if (result != null)
				return result;

			foreach (var prefix in namespaceUsings)
			{
				result = Binder.BindType(Name.Qualify(prefix).FullName);
				if (result != null)
					return result;
			}

			return null;
		}
	}
}

