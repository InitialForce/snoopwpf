# ARCHITECTURE CHANGE NOTICE — Pluggable suggestion translator

**Date**: 2026-04-16
**Affects**: PRD-v5-MVP.md §7.5 (`suggestion` schema), §4.5 (brokered
response pipeline). BEADS-MVP.md: beads M1-01 (contracts), M2-21
(broker scaffolding).
**Depends on**: ARCHITECTURE-CHANGE-2026-04-16-BROKERED-MODE.md.
**Driver**: consumer-side PRD at
`/c/work/desktop/wpf-mcp/PRD-snoop-integration.md` §4.5.

**Read this after the brokered-mode change notice.** Then re-read
PRD-v5-MVP.md §7.5 for the existing `suggestion` schema.

---

## What changes

Suggestions emitted by the library today carry `{ tool, args }`.
Brokered consumers need to rewrite some of those tool names (e.g. a
consumer-side `UiMcpHost` translates the generic
`broker_launch_target` suggestion into its own product-specific
launch tool) before forwarding the response frame to the MCP client.

Today that rewrite has to happen as post-hoc string matching on the
consumer side. That is fragile — every new upstream suggestion tool
name is a silent consumer breakage.

**The fix**: the library exposes a pluggable `ISuggestionTranslator`
extension point. Consumers that need translation register an
implementation; everyone else gets the default (no-op) translator
and is unaffected.

Snoopwpf stays generic. No consumer-specific tool name is ever
hardcoded in upstream code.

## New contract (Contracts package)

```csharp
// SnoopWPF.Agent.Contracts

/// <summary>
/// Coarse-grained classification of a suggestion, used by translators
/// to decide whether and how to rewrite the tool name.
/// </summary>
public enum SuggestionCategory
{
    /// <summary>
    /// The suggestion references a broker-lifecycle operation (launch,
    /// restart, attach). Consumers typically map these to product-
    /// specific tool names (e.g. `broker_launch_target` →
    /// `<product>_launch`).
    /// </summary>
    BrokerLifecycle = 0,

    /// <summary>
    /// The suggestion recommends inspecting state (screenshot,
    /// property read, `wpf_get_tree`) because the last outcome was
    /// uncertain.
    /// </summary>
    StateInspection = 1,

    /// <summary>
    /// The suggestion recommends retrying the call after the cause
    /// has been fixed (e.g. `LOCATOR_NOT_FOUND` + retry same tool).
    /// </summary>
    Retry = 2,

    /// <summary>
    /// The suggestion relates to redaction / sensitive content
    /// (e.g. re-issue with `retainSensitive: false`).
    /// </summary>
    Redaction = 3,

    /// <summary>
    /// Other categories. Translators should pass these through.
    /// </summary>
    Other = 99,
}

/// <summary>
/// Structured suggestion emitted as part of a state-delta envelope
/// (snoopwpf PRD §7.5). Extends the existing `{tool, args}` schema
/// with a category and an optional rationale.
/// </summary>
public sealed record Suggestion(
    string Tool,
    IReadOnlyDictionary<string, object?> Args,
    SuggestionCategory Category,
    string? Rationale = null);

/// <summary>
/// Context passed alongside every suggestion being translated. The
/// library fills this in before calling the translator.
/// </summary>
public sealed record SuggestionTranslationContext(
    string OriginalToolName,
    string? FailureReason,
    string? ErrorCode);

/// <summary>
/// Consumer-side hook for translating library-emitted suggestions
/// into consumer-specific suggestions. Consumers register one
/// implementation; default is <see cref="IdentitySuggestionTranslator"/>.
/// </summary>
public interface ISuggestionTranslator
{
    /// <summary>
    /// Translate an upstream suggestion. Return the input unchanged
    /// to accept the library default. Return a new <see cref="Suggestion"/>
    /// to rewrite. Return <c>null</c> to drop the suggestion entirely
    /// (rare; most unknown categories should be passed through).
    /// </summary>
    Suggestion? Translate(Suggestion upstream, SuggestionTranslationContext context);
}

/// <summary>
/// Default pass-through translator. Used when no consumer-specific
/// translator is registered.
/// </summary>
public sealed class IdentitySuggestionTranslator : ISuggestionTranslator
{
    public Suggestion? Translate(Suggestion upstream, SuggestionTranslationContext context)
        => upstream;
}
```

## Wiring (BrokerHost package)

```csharp
// SnoopWPF.Agent.BrokerHost

public sealed class BrokerHostOptions
{
    /// <summary>
    /// Registered suggestion translator. Defaults to
    /// <see cref="IdentitySuggestionTranslator"/>; consumers that need
    /// product-specific tool-name rewriting supply their own.
    /// </summary>
    public ISuggestionTranslator SuggestionTranslator { get; init; }
        = new IdentitySuggestionTranslator();

    // ... other options (pipe-router config, tool-proxy registration, etc.)
}
```

The broker applies the translator to every outbound frame that
carries a `suggestion`, immediately before forwarding the frame to
the MCP client. Translator exceptions are caught, logged, and fall
back to the upstream suggestion (fail-open).

Ordering:

```
target -> frame ingress -> policy redaction -> SUGGESTION TRANSLATOR -> MCP client
```

## Wiring (Server package, co-located parity)

The co-located `SnoopWPF.Agent.Server` path also emits suggestions,
so the same extension point lives on `SnoopAgentOptions`. Co-located
consumers that host their own broker-equivalent can register a
translator there too. Default is identity; no behaviour change for
existing CoLocated callers.

## Reference consumer (non-normative — lives in the consumer repo)

```csharp
// UiMcpHost (MC-side; shown here only as an illustration of the hook —
//  this code stays in /c/work/desktop/wpf-mcp, NOT in snoopwpf).

internal sealed class McSuggestionTranslator : ISuggestionTranslator
{
    private static readonly IReadOnlyDictionary<string, string> _map
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["broker_launch_target"]  = "mc_launch",
            ["broker_restart_target"] = "mc_restart",
        };

    public Suggestion? Translate(Suggestion u, SuggestionTranslationContext ctx)
    {
        if (u.Category != SuggestionCategory.BrokerLifecycle) return u;
        if (_map.TryGetValue(u.Tool, out var mcTool))
            return u with { Tool = mcTool };
        if (u.Tool == "broker_attach_existing")
            return u with
            {
                Tool = "_deferred_mc_attach_pid",
                Rationale = "mc_attach_pid deferred post-v1.1; try mc_restart"
            };
        return u; // unknown broker_* suggestion — pass through + broker logs it
    }
}
```

Important: no snoopwpf code references any consumer-specific tool
name. Every string literal belongs to the consumer.

## Beads affected

### Update in place

- **M1-01 SessionPolicy + contracts** — add `SuggestionCategory`
  enum, `Suggestion` record (extended shape), `ISuggestionTranslator`
  interface, and `IdentitySuggestionTranslator` default to the
  Contracts package. Acceptance: every library-emitted suggestion
  sets a category; unit tests cover the identity translator and a
  test double that rewrites one category.

- **M2-21 `SnoopWPF.Agent.BrokerHost` broker scaffolding** — extend
  acceptance to include:
  - `BrokerHostOptions.SuggestionTranslator` property (defaulting to
    `IdentitySuggestionTranslator`).
  - Frame egress pipeline applies the translator before writing to
    the MCP client stream.
  - Translator exceptions are caught and logged (fail-open).
  - Unit test: translator that swaps a specific tool name is visible
    to an in-process MCP client.
  - Unit test: null translator return drops the suggestion.

### Scope note

The existing `suggestion` emission sites in snoopwpf (e.g. the
`TARGET_NOT_RUNNING` → `broker_launch_target` mapping in PRD §7.5)
must be updated to populate the new `Category` field. This is a
contracts change, not a new suggestion. Every existing suggestion
gets a category set based on the table below:

| Existing `failureReason` / error path | New `Category` |
|--------------------------------------|----------------|
| `TARGET_NOT_RUNNING` → `broker_launch_target` | `BrokerLifecycle` |
| `TARGET_CRASHED` → `broker_restart_target` (if added) | `BrokerLifecycle` |
| `LOCATOR_NOT_FOUND` → `wpf_query` | `Retry` |
| `STATE_UNCERTAIN` → `wpf_get_tree` | `StateInspection` |
| `SENSITIVE_CONTENT_REDACTED` → re-issue with `retainSensitive: false` | `Redaction` |
| anything else | `Other` |

## PRD amendments

- **PRD-v5-MVP.md §7.5** — update the `suggestion` schema to include
  `category: SuggestionCategory` and optional `rationale`. Add a
  subsection pointing to this change notice for the extension point
  contract.
- **PRD-v5-MVP.md §4.5** (or wherever the brokered response pipeline
  is normatively described) — add the "translator-applied-before-egress"
  ordering note.

## Tracker sync (`br`) commands

```bash
# Update existing beads
br update M1-01 --add-note "2026-04-16: add SuggestionCategory + ISuggestionTranslator contract + IdentitySuggestionTranslator default; see ARCHITECTURE-CHANGE-2026-04-16-SUGGESTION-TRANSLATOR.md"

br update M2-21 --add-note "2026-04-16: extend acceptance — BrokerHostOptions.SuggestionTranslator, egress-pipeline application, fail-open exception handling, unit tests; see ARCHITECTURE-CHANGE-2026-04-16-SUGGESTION-TRANSLATOR.md"

# No new beads — this lands inside existing M1-01 and M2-21 scopes.

br sync --flush-only
```

## Rationale

### Why an upstream extension point instead of consumer post-processing

Every consumer that hosts a broker needs to translate some subset of
generic broker-lifecycle suggestions. Today each consumer reinvents
string-match rewriting in a different way. Each upstream rename
silently breaks every consumer.

An upstream contract moves the coupling into the type system. The
`SuggestionCategory` enum gives consumers a structured switch. New
upstream suggestion tool names are backward-compatible — the
category is set correctly, and consumers that don't know the new
name pass it through instead of dropping it.

### Why `SuggestionCategory` instead of just raw tool names

Tool names are implementation details that can churn. Categories are
stable taxonomic labels. A consumer translating a `BrokerLifecycle`
suggestion doesn't need to know whether it was
`broker_launch_target` or `broker_spawn_target` or `broker_create_session`
— it can pattern-match on the category and, for known tool names,
apply a specific rewrite.

### Why the interface lives in Contracts, not BrokerHost

Contracts is the shared-surface package. Consumers that build their
own broker (not using `SnoopWPF.Agent.BrokerHost`) still need the
interface. CoLocated mode also uses it (for parity). Placing it in
the lowest-level package is the correct layering.

### Scope containment

This change is intentionally small. No new suggestion emission
sites. No behaviour change for existing consumers. New API surface
is three types (`SuggestionCategory`, extended `Suggestion`,
`ISuggestionTranslator`) plus one default impl.

## Questions / follow-ups

1. **Threading**: does the translator run on the dispatcher thread
   or the pipe-egress thread? Proposal: pipe-egress thread.
   Translators are expected to be pure/cheap; they must not call
   back into dispatcher-bound APIs. Document this in the interface
   XML docs.
2. **Serialization**: `SuggestionCategory` serializes as its string
   name (not the int). Enables forward-compatibility when adding new
   categories.
3. **Multiple translators**: not supported in v1. One translator per
   session. If chaining is needed later, we ship a
   `CompositeSuggestionTranslator`.

---

*End of change notice. Apply M1-01 / M2-21 updates and re-read
PRD-v5-MVP.md §7.5 before continuing either bead.*
