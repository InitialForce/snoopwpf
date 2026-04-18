# Future work — improvement ideas

Five concrete directions that would extend the agentic harness beyond the current 33-tool `wpf_*` surface. Each is self-contained (no entanglement with the others) and has a plausible motivating user need documented alongside implementation sketch.

These are **ideas, not commitments.** They are listed here so downstream consumers can weigh in on priority, and so contributors looking for a meaningful first task have a starting point.

---

## 1. JSONSchema tool catalog as a build artifact

**Problem.** Today, agents learn the `wpf_*` surface from `llms.txt` (human-curated) and from MCP's `tools/list` at runtime. There is no single source of truth that third-party harnesses (Copilot, Cursor, custom orchestrators) can import at design time. Drift between `llms.txt` and the actual registered tools is detected only at runtime.

**Proposal.** Emit `snoopwpf-tools.schema.json` as a build artifact from `SnoopWPF.Agent.Tools`, generated from the `[McpServerTool]`, `[Description]`, and parameter attributes via a Roslyn source generator. Publish as:

- A file inside the `InitialForce.SnoopAgent.Tools` NuGet (`content/snoopwpf-tools.schema.json`).
- A well-known URL: `https://raw.githubusercontent.com/InitialForce/snoopwpf/main/snoopwpf-tools.schema.json`.
- A `tools/catalog` MCP resource inside the broker, for dynamic discovery.

**Wins.**
- Agents can validate tool calls *before* sending them.
- Static typing in TypeScript/Python SDKs (auto-generated bindings).
- Keeps `llms.txt` honest — CI check that every schema entry has a corresponding prose line.

**Effort.** ~2 weeks. Roslyn source generator + one CI workflow. Zero runtime cost.

---

## 2. MCP Inspector sidecar

**Problem.** When an agent's plan fails (e.g. a binding chain unexpectedly evaluates to `null`), the human operator has no live view. Reading the broker's log after the fact is post-mortem debugging. There is no equivalent of Chrome DevTools for a running brokered session.

**Proposal.** A new sidecar `snoop-inspector.exe` that:

- Attaches to a live brokered session (reuses the same `SessionManifest` discovery flow).
- Renders the visual tree in a Snoop-like UI, live-polled.
- Shows a request/response log (latest 1000 `wpf_*` calls with timings).
- Exposes a binding-chain viewer (click any element → see `DataContext` → `Parent.DataContext` → etc. as a live tree).
- Ships as a standalone WPF app, one extra NuGet (`InitialForce.SnoopAgent.Inspector`).

**Wins.**
- Operators can watch what the agent sees, in real time.
- Replaces the need to re-run a session in "debug mode" — the inspector attaches to a production-like session.
- Dogfoods the `wpf_*` surface: every feature the inspector needs must exist as a stable tool.

**Effort.** ~6 weeks for an MVP (tree + log + element detail). Reuses existing Snoop UI components.

---

## 3. OpenTelemetry spans for every tool call

**Problem.** Performance debugging of agent sessions requires manual instrumentation or log parsing. There is no way to ask "which tool took the longest across the last 100 sessions?" without building custom observability.

**Proposal.** Wrap every `wpf_*` tool invocation in an `ActivitySource` span:

- Span name = tool name (e.g. `wpf_find_elements`).
- Attributes = input parameters (scrubbed — no PII/payloads), `target.pid`, `target.title`, `broker.sessionId`, `protocol.version`.
- Status = ok / error, with `SnoopErrorCode` or `FailureReason` as the error-type attribute.
- Events = HMAC handshake, injection, first tool call.

Ship with an OTLP exporter opt-in via `SnoopAgentOptions.EnableOpenTelemetry`. No tracing SDK dependency in the default package — exporter lives in `InitialForce.SnoopAgent.OpenTelemetry` (new tiny package).

**Wins.**
- Dogfood existing OTEL infrastructure (Tempo, Jaeger, Honeycomb, Datadog).
- Long-running agent sessions become inspectable at scale.
- CI smoke jobs can diff p95 latencies per tool across PRs.

**Effort.** ~1 week. `Activity` + `ActivitySource` are in-box; OTLP exporter is a thin wrapper.

---

## 4. VS Code Debug Adapter Protocol (DAP) bridge

**Problem.** Agents operate through reasoning-and-action loops. When something goes wrong, a human often has to re-read the transcript and guess where the plan diverged. There is no "step-through" experience.

**Proposal.** A new `snoop-dap.exe` that implements DAP against a brokered session:

- **Breakpoints:** "stop when this element's `IsEnabled` changes," "stop on next `MUTATION_DISABLED` error," "stop on first `wpf_click` against node `0:42`."
- **Step-over:** step one `wpf_*` call at a time; inspect tree deltas between steps.
- **Variables pane:** shows the current visual tree, bindings, recent tool responses.
- **Call stack:** most recent N tool calls with their arguments.

**Wins.**
- VS Code / Rider / any DAP-speaking IDE becomes the agent debugger.
- Moves the human from "read logs after failure" to "watch the plan execute step-by-step."
- Complements the Inspector (Idea #2) by targeting engineers rather than operators.

**Effort.** ~8 weeks. DAP is well-specced; most effort is in mapping `wpf_*` concepts to DAP idioms (scopes, variables, threads).

---

## 5. `wpf_record` / `wpf_replay` for deterministic regression tests

**Problem.** Agent-driven tests are non-deterministic by nature: the agent may choose different tools across runs, so comparing outputs is fragile. But the *effect* of a sequence of tool calls on a given app build should be deterministic (same inputs → same tree deltas). There is no way to record a sequence and replay it against a new build.

**Proposal.** Two new tools:

- `wpf_record(sessionId, outputPath)` — begin recording every `wpf_*` call with its full input + output + tree-version-delta to a newline-delimited JSON file.
- `wpf_replay(recordingPath, strictMode)` — play back a recording against the currently-attached app; report any divergence in response fields or tree shape.

**Strict mode** asserts byte-equality on responses. **Loose mode** tolerates timing and nonce fields but asserts structural identity on tree deltas. Both produce a machine-readable diff of what changed.

**Wins.**
- CI can guard "PR changes MotionCatalyst's Settings screen" by re-running a recorded agent session.
- Agents can self-regression-test their own plans before committing UI changes.
- Unlocks "golden-path" test authoring via agent exploration — record once, replay forever.

**Effort.** ~3 weeks. Recording is cheap (observer pattern on the tool dispatcher). Replay is harder because of PID/nodeId/token differences — needs a rebinding layer that remaps session-local identifiers.

---

## How to prioritize

If forced to pick one: **Idea #1 (JSONSchema catalog)** has the highest leverage-per-week-of-effort, since every downstream consumer (including the other four ideas) benefits from a machine-readable tool surface.

**Idea #3 (OTEL)** is a close second — minimal code, maximum operational payoff.

**Ideas #2, #4, #5** each require substantial investment but unlock qualitatively new workflows. The order below is one plausible roadmap:

```
Phase A (2-3 weeks):  #1 JSONSchema catalog  +  #3 OTEL spans
Phase B (6-8 weeks):  #2 MCP Inspector
Phase C (6-8 weeks):  #4 DAP bridge
Phase D (3-4 weeks):  #5 Record/Replay
```

Nothing in here blocks anything in the current 33-tool surface. All five additions are opt-in, additive, and backwards-compatible.

---

## Contributing

If any of these ideas resonates and you want to take a run at implementation, open a discussion first at [github.com/InitialForce/snoopwpf/discussions](https://github.com/InitialForce/snoopwpf/discussions) — the design space for each is wider than this document sketches, and we want to avoid parallel wasted effort.
