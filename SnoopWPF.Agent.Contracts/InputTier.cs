namespace SnoopWPF.Agent.Contracts;

/// <summary>
/// Input tier governing the maximum automation capability permitted for this session.
/// </summary>
/// <remarks>
/// Tiers are ordered: <see cref="L0ReadOnly"/> ≤ <see cref="L0"/> ≤ <see cref="L1"/>.
/// L2 / L3 / L4 are deferred to v2.0.
/// </remarks>
public enum InputTier
{
    /// <summary>
    /// Read-only inspection only — no input simulation of any kind.
    /// Mandatory in injection mode (S7).
    /// </summary>
    L0ReadOnly = 0,

    /// <summary>
    /// L0: Automation-API-based input (UIAutomation, ICommand). No raw Win32 input.
    /// </summary>
    L0 = 1,

    /// <summary>
    /// L1: Deterministic WPF-native input (RaiseEvent, InvokePattern). Superset of L0.
    /// </summary>
    L1 = 2,
}
