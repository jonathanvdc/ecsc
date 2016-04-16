using System;
using Loyc.Collections;

namespace Flame.Ecs
{
	/// <summary>
	/// A data structure that represents a qualified or unqualified name.
	/// </summary>
	/// <remarks>
	/// This data structure is essentially a singly linked list.
	/// </remarks>
	public sealed class QualifiedName : IEquatable<QualifiedName>
	{
		public QualifiedName(string Qualifier, QualifiedName Name)
		{
			this.Qualifier = Qualifier;
			this.Name = Name;
		}
		public QualifiedName(string Name)
		{
			this.Qualifier = Name;
			this.Name = null;
		}

		/// <summary>
		/// Gets this qualified name's qualifier, or the
		/// unqualified name, if this name is not qualified.
		/// </summary>
		public string Qualifier { get; private set; }

		/// <summary>
		/// Gets the name that is qualified by the qualifier.
		/// </summary>
		public QualifiedName Name { get; private set; }

		/// <summary>
		/// Gets a value indicating whether this name is a qualified name, 
		/// rather than an unqualified name.
		/// </summary>
		/// <value><c>true</c> if this name is qualified; otherwise, <c>false</c>.</value>
		public bool IsQualified { get { return Name != null; } }

		/// <summary>
		/// Gets a value indicating whether this name is empty: it is both
		/// unqualified, and its name is either null or the empty string.
		/// </summary>
		/// <value><c>true</c> if this name is empty; otherwise, <c>false</c>.</value>
		public bool IsEmpty { get { return !IsQualified && string.IsNullOrEmpty(Qualifier); } }

		/// <summary>
		/// Gets this qualified name's full name.
		/// </summary>
		public string FullName { get { return Name == null ? Qualifier : MemberExtensions.CombineNames(Qualifier, Name.FullName); } }

		/// <summary>
		/// Qualifies this name with an additional qualifier.
		/// A new instance is returned that represents the 
		/// concatenation of said qualifier and this
		/// qualified name.
		/// </summary>
		public QualifiedName Qualify(string PreQualifier)
		{
			return new QualifiedName(PreQualifier, this);
		}

		/// <summary>
		/// Qualifies this name with an additional qualifier.
		/// A new instance is returned that represents the 
		/// concatenation of said qualifier and this
		/// qualified name.
		/// </summary>
		public QualifiedName Qualify(QualifiedName PreQualifier)
		{
			if (!PreQualifier.IsQualified)
				return Qualify(PreQualifier.Qualifier);
			else
				return Qualify(PreQualifier.Name).Qualify(PreQualifier.Qualifier);
		}

		public bool Equals(QualifiedName Other)
		{
			return Qualifier == Other.Qualifier && Name == null 
				? Other.Name == null 
				: (Other.Name != null && Name.Equals(Other.Name));
		}

		public override bool Equals(object Other)
		{
			return Other is QualifiedName && Equals((QualifiedName)Other);
		}

		public override int GetHashCode()
		{
			if (IsQualified)
				return Qualifier.GetHashCode() ^ Name.GetHashCode() << 1;
			else
				return Qualifier.GetHashCode();
		}

		public override string ToString()
		{
			return FullName;
		}
	}
}

