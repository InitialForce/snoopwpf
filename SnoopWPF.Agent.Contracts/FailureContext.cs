namespace SnoopWPF.Agent.Contracts;

using System.Collections.Generic;

/// <summary>
/// Contextual information passed to <see cref="ISuggestionTranslator.Translate"/>
/// to allow per-node and per-call suggestion customisation.
/// </summary>
/// <remarks>
/// <para>
/// <c>Locator</c> mirrors the <c>WpfLocator?</c> parameter that was previously passed
/// directly to <c>FailureReasonDescriptor.Suggest</c>.  It is wrapped here so that
/// additional context (e.g. tool name, session mode) can be added in future without
/// changing the <see cref="ISuggestionTranslator"/> signature.
/// </para>
/// <para>
/// <c>FailureExtra</c> is an optional, caller-supplied dictionary of free-form hint
/// values (e.g. <c>"propertyName"</c>, <c>"expectedTier"</c>).  The default translator
/// ignores extra hints; custom translators may use them to produce richer suggestions.
/// </para>
/// </remarks>
public sealed class FailureContext
{
    /// <summary>
    /// The element locator associated with the failing operation, or
    /// <see langword="null"/> when no element context is available.
    /// </summary>
    public WpfLocator? Locator { get; init; }

    /// <summary>
    /// Optional free-form hint values supplied by the caller.
    /// Default: empty dictionary (never null).
    /// </summary>
    public IReadOnlyDictionary<string, string> FailureExtra { get; init; }
        = new Dictionary<string, string>();

    /// <summary>Empty context (no locator, no extra hints).</summary>
    public static readonly FailureContext Empty = new();

    /// <summary>
    /// Convenience factory: creates a context that carries only a locator.
    /// </summary>
    public static FailureContext FromLocator(WpfLocator? locator) =>
        locator is null ? Empty : new FailureContext { Locator = locator };
}
