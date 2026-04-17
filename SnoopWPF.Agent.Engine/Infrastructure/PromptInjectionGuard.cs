namespace SnoopWPF.Agent.Engine.Infrastructure;

using System;
using System.Security.Cryptography;

/// <summary>
/// Wraps raw user-controlled strings in a per-message nonce-keyed trust-boundary marker
/// before they are embedded in LLM-facing output (e.g. wpf_get_visual_tree, wpf_get_binding_info).
/// </summary>
/// <remarks>
/// <para>
/// A malicious target application can inject arbitrary text into WPF element
/// ToString() output and DataContext.ToString() values.  Without a trust boundary,
/// that text reaches the LLM in the same band as the system prompt, enabling
/// prompt-injection attacks (5x5 report finding #6, ADV-C1/C2).
/// </para>
/// <para>
/// The markers include a per-call cryptographic nonce so the end marker cannot be
/// forged: an attacker would need to know the nonce chosen at call time to craft a
/// premature «UD_END:{nonce}» sequence, which is impossible without knowing the
/// CSPRNG output (R6-L2B finding).
/// </para>
/// <para>
/// This class is a pure function with no WPF dependencies — safe to use on any thread
/// and straightforward to unit-test.
/// </para>
/// </remarks>
public static class PromptInjectionGuard
{
    /// <summary>
    /// Wraps <paramref name="raw"/> in nonce-keyed trust-boundary markers that allow an
    /// LLM to distinguish user-controlled content from system instructions.
    /// The nonce is unique per call, making the end marker unforgeable by the target app.
    /// </summary>
    /// <param name="raw">The user-controlled string to quote. <see langword="null"/> or empty returns <see cref="string.Empty"/>.</param>
    /// <returns>A marked string safe for inclusion in LLM-consumed output.</returns>
    public static string Quote(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        string nonce;
        do
        {
            nonce = GenerateNonce();
        }
        while (raw!.IndexOf(nonce, StringComparison.Ordinal) >= 0);

        return $"\u00ABUD_BEGIN:{nonce}\u00BB\n{raw}\n\u00ABUD_END:{nonce}\u00BB";
    }

    private static string GenerateNonce()
    {
        var bytes = new byte[8];
#if NET6_0_OR_GREATER
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes);
#else
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(bytes);
        }

        var chars = new char[16];
        for (var i = 0; i < 8; i++)
        {
            var b = bytes[i];
            chars[i * 2] = HexChar(b >> 4);
            chars[(i * 2) + 1] = HexChar(b & 0xF);
        }

        return new string(chars);
#endif
    }

#if !NET6_0_OR_GREATER
    private static char HexChar(int nibble) =>
        (char)(nibble < 10 ? '0' + nibble : 'A' + nibble - 10);
#endif
}
