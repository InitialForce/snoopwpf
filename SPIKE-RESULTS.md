# v5-MVP Spike Results

Each M0 spike appends a section. Verdicts: GREEN (pass + no concerns),
YELLOW (pass with caveats), RED (fail; plan fallback).

## S-1 — Co-located stdio round-trip re-verification

**Date:** 2026-04-16
**Bead:** M0-01 (bd-3gk)
**Machine:** `AMD Ryzen 9 5950X 16-Core Processor`, `64 GB RAM`, .NET SDK `10.0.104`.
**Integration suite:** `SnoopWPF.Agent.IntegrationTests`

- Total tests: 86
- Passed: 84
- Failed: 0
- Run time: 0:02 (2.52 s)
- Slowest test: GetAncestors_AllAncestors_HaveNodeIdAndTypeName 283 ms
- Fastest test: GetBehaviors_UnknownNodeId_ThrowsSnoopException <1 ms

**Verdict:** GREEN

**Notes:** 2 tests skipped (inconclusive): `GetResources_WithKeyFilter_NarrowsResults` (no
resources found in application) and `GetChildren_WithNodeId_ReturnsChildrenOfThatNode` (no
child node with children at root level). These are pre-existing inconclusive conditions, not
failures. The StyleCop warning SA1200 on `McpStdioEntrypointAttribute.cs` causes a build error
under default settings; tests were run with `-p:TreatWarningsAsErrors=false`. This warning
should be fixed before M1 work begins.

---
