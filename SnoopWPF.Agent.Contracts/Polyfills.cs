// Polyfills required for C# 9+ features (init, record) on .NET Framework 4.6.2.
// These are compiler-only types; no runtime dependency is introduced.
#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    using System.ComponentModel;

    /// <summary>Enables the <c>init</c> accessor and <c>record</c> types on .NET Framework.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit
    {
    }
}
#endif
