namespace SnoopWPF.Agent.Engine.StateDelta;

using System.Collections.Generic;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;

/// <summary>
/// Maps a <see cref="FailureReason"/> value to a machine-executable <see cref="SuggestionDto"/>
/// per PRD §7.4 / FD-3.  Prose suggestions are not permitted; every suggestion must carry
/// a concrete tool name and args so the calling agent can act without human intervention.
/// </summary>
internal static class FailureReasonDescriptor
{
    /// <summary>
    /// Returns the canonical <see cref="SuggestionDto"/> for <paramref name="reason"/>, or
    /// <see langword="null"/> when no automated remediation exists (e.g. session-level config
    /// changes must be made by the operator, not by a retry loop).
    /// </summary>
    /// <param name="reason">The failure reason to map.</param>
    /// <param name="ctx">
    /// Optional locator context used to populate Args when the suggestion is locator-relative.
    /// Pass <see langword="null"/> when no element context is available.
    /// </param>
    public static SuggestionDto? Suggest(FailureReason reason, WpfLocator? ctx)
    {
        var locator = ctx?.Raw ?? string.Empty;

        return reason switch
        {
            // ── Element-resolution failures ─────────────────────────────────────────
            FailureReason.ElementNotFound => new SuggestionDto
            {
                Tool = "wpf_find_elements",
                Args = new List<NameValuePairDto>
                {
                    new() { Name = "query", Value = locator },
                },
            },

            FailureReason.LocatorAmbiguous => new SuggestionDto
            {
                Tool = "wpf_find_elements",
                Args = new List<NameValuePairDto>
                {
                    new() { Name = "query", Value = locator },
                    new() { Name = "hint", Value = "Refine locator to match exactly one element." },
                },
            },

            // ── Visibility / enabled / viewport ────────────────────────────────────
            FailureReason.ElementNotVisible => new SuggestionDto
            {
                Tool = "wpf_wait_for_property",
                Args = new List<NameValuePairDto>
                {
                    new() { Name = "locator",       Value = locator },
                    new() { Name = "propertyName",  Value = "IsVisible" },
                    new() { Name = "expectedValue", Value = "true" },
                    new() { Name = "timeoutMs",     Value = "5000" },
                },
            },

            // Deliberate divergence from PRD §7.4: wait-for-property is more actionable than inspect-element.
            FailureReason.ElementNotEnabled => new SuggestionDto
            {
                Tool = "wpf_wait_for_property",
                Args = new List<NameValuePairDto>
                {
                    new() { Name = "locator",       Value = locator },
                    new() { Name = "propertyName",  Value = "IsEnabled" },
                    new() { Name = "expectedValue", Value = "true" },
                    new() { Name = "timeoutMs",     Value = "5000" },
                },
            },

            FailureReason.ElementOutsideViewport => new SuggestionDto
            {
                Tool = "wpf_select_item",
                Args = new List<NameValuePairDto>
                {
                    new() { Name = "locator", Value = locator },
                    new() { Name = "hint",    Value = "Use the scrollable parent container as locator to bring item into view." },
                },
            },

            // ── Command / pattern failures ──────────────────────────────────────────
            FailureReason.CannotExecuteCommand => new SuggestionDto
            {
                Tool = "wpf_resolve_binding",
                Args = new List<NameValuePairDto>
                {
                    new() { Name = "locator",      Value = locator },
                    new() { Name = "propertyName", Value = "Command" },
                },
            },

            FailureReason.PatternNotSupported => new SuggestionDto
            {
                Tool = "wpf_inspect_element",
                Args = new List<NameValuePairDto>
                {
                    new() { Name = "locator", Value = locator },
                    new() { Name = "hint",    Value = "Check supported automation patterns via wpf_get_properties." },
                },
            },

            // ── Tier / strategy failures ────────────────────────────────────────────
            FailureReason.TierMismatch => new SuggestionDto
            {
                Tool = "wpf_execute_command",
                Args = new List<NameValuePairDto>
                {
                    new() { Name = "hint", Value = "Fallback to L0 read-only inspection; command execution not available at current tier." },
                },
            },

            // ── Transient / state-change failures ──────────────────────────────────
            // Deliberate divergence from PRD §7.4: wait-for-property is more actionable than inspect-element.
            FailureReason.StateUnchanged => new SuggestionDto
            {
                Tool = "wpf_wait_for_property",
                Args = new List<NameValuePairDto>
                {
                    new() { Name = "locator",   Value = locator },
                    new() { Name = "hint",      Value = "Verify expected property and value then retry." },
                    new() { Name = "timeoutMs", Value = "3000" },
                },
            },

            FailureReason.DispatcherBusy => new SuggestionDto
            {
                Tool = "wpf_wait_for_property",
                Args = new List<NameValuePairDto>
                {
                    new() { Name = "locator",       Value = locator },
                    new() { Name = "propertyName",  Value = "IsEnabled" },
                    new() { Name = "expectedValue", Value = "true" },
                    new() { Name = "timeoutMs",     Value = "2000" },
                    new() { Name = "hint",          Value = "Dispatcher was busy; wait for UI thread to settle." },
                },
            },

            // ── Process-level failure ───────────────────────────────────────────────
            FailureReason.TargetNotRunning => new SuggestionDto
            {
                Tool = "broker_launch_target",
                Args = new List<NameValuePairDto>(),
            },

            // ── Session-config failures — no auto-remediation ───────────────────────
            FailureReason.AutomationDisabled => null,
            FailureReason.MutationDisabled => null,

            _ => null,
        };
    }
}
