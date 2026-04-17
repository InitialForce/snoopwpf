namespace SnoopWPF.Agent.Engine.Infrastructure;

/// <summary>
/// Wraps raw user-controlled strings in a trust-boundary marker before they are
/// embedded in LLM-facing output (e.g. wpf_get_visual_tree, wpf_get_binding_info).
/// </summary>
/// <remarks>
/// <para>
/// A malicious target application can inject arbitrary text into WPF element
/// ToString() output and DataContext.ToString() values.  Without a trust boundary,
/// that text reaches the LLM in the same band as the system prompt, enabling
/// prompt-injection attacks (5x5 report finding #6, ADV-C1/C2).
/// </para>
/// <para>
/// This class is a pure function with no WPF dependencies — safe to use on any thread
/// and straightforward to unit-test.
/// </para>
/// </remarks>
public static class PromptInjectionGuard
{
    private const string Begin = "\u00AB USER_DATA_BEGIN \u00BB";
    private const string End = "\u00AB USER_DATA_END \u00BB";

    /// <summary>
    /// Wraps <paramref name="raw"/> in trust-boundary markers that allow an LLM to
    /// distinguish user-controlled content from system instructions.
    /// Any occurrence of the marker sequences inside <paramref name="raw"/> is escaped
    /// with a leading asterisk so the markers cannot be forged by the target app.
    /// </summary>
    /// <param name="raw">The user-controlled string to quote. <see langword="null"/> is treated as empty.</param>
    /// <returns>A marked string safe for inclusion in LLM-consumed output.</returns>
    public static string Quote(string? raw)
    {
        // Escape any marker sequences embedded in the raw user data so they cannot
        // be mistaken for the real boundary markers by the LLM.
        // string.Replace(string, string) performs ordinal replacement — CA1307 is
        // intentionally suppressed: a StringComparison overload is unavailable on net462.
#pragma warning disable CA1307 // Specify StringComparison for clarity
        var text = (raw ?? string.Empty)
            .Replace(Begin, "*" + Begin)
            .Replace(End, "*" + End);
#pragma warning restore CA1307

        return Begin + "\n" + text + "\n" + End;
    }
}
