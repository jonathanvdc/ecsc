using System;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace Flame.Ecs
{
	/// <summary>
	/// A type of object that maps qualified names to types.
	/// </summary>
	public class QualifiedBinder
	{
		private QualifiedBinder(
			IBinder Binder, List<QualifiedName> namespaceUsings, 
			Dictionary<string, QualifiedName> nameAliases)
		{
			this.Binder = Binder;
			this.namespaceUsings = namespaceUsings;
			this.nameAliases = nameAliases;
			this.resolvedTypeCache = new ConcurrentDictionary<QualifiedName, IType>();
		}
		public QualifiedBinder(IBinder Binder)
			: this(Binder, new List<QualifiedName>(), new Dictionary<string, QualifiedName>())
		{ }

		/// <summary>
		/// Gets this qualified name binder's underlying binder.
		/// </summary>
		public IBinder Binder { get; private set; }

		private List<QualifiedName> namespaceUsings;
		private Dictionary<string, QualifiedName> nameAliases;
		private ConcurrentDictionary<QualifiedName, IType> resolvedTypeCache;

		/// <summary>
		/// Adds the given qualified name to the list of used namespaces.
		/// </summary>
		public QualifiedBinder UseNamespace(QualifiedName Name)
		{
			var newUsings = new List<QualifiedName>(namespaceUsings);
			newUsings.Add(Name);
			return new QualifiedBinder(Binder, newUsings, nameAliases);
		}

		/// <summary>
		/// Creates an alias for a qualified name.
		/// </summary>
		public QualifiedBinder AliasName(string Alias, QualifiedName Name)
		{
			var newAliases = new Dictionary<string, QualifiedName>(nameAliases);
			newAliases[Alias] = Name;
			return new QualifiedBinder(Binder, namespaceUsings, nameAliases);
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

			QualifiedName aliasedQualifier;
			if (nameAliases.TryGetValue(Name.Qualifier, out aliasedQualifier))
				Name = Name.Qualify(aliasedQualifier);

			var result = Binder.BindType(Name.FullName);
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

