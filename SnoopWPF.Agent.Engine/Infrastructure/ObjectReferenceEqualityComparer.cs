// Polyfill for .NET Framework 4.6.2: ReferenceEqualityComparer was introduced in .NET 5.
#if !NET5_0_OR_GREATER
namespace SnoopWPF.Agent.Engine;

using System.Collections.Generic;
using System.Runtime.CompilerServices;

/// <summary>
/// An equality comparer that uses reference equality (<see cref="object.ReferenceEquals"/>)
/// and identity hash codes. Polyfill for <c>ReferenceEqualityComparer</c> (.NET 5+).
/// </summary>
internal sealed class ObjectReferenceEqualityComparer : IEqualityComparer<object>
{
    /// <summary>Singleton instance.</summary>
    public static readonly ObjectReferenceEqualityComparer Instance = new();

    private ObjectReferenceEqualityComparer()
    {
    }

    bool IEqualityComparer<object>.Equals(object? x, object? y) => ReferenceEquals(x, y);

    int IEqualityComparer<object>.GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
}
#endif
