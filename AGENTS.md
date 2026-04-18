# AGENTS.md

This project publishes an MCP server for WPF inspection. AI agents and tool-builders
should read `llms.txt` (at repo root) for the canonical agent-facing contract:
tool catalog, preconditions, error codes, and state machine.
`llms.txt` follows the llmstxt.org convention for AI-agent-optimized documentation — it is the single-file surface an agent should read before using the wpf_* tools.

Human documentation: `README.md`.
Deep architecture: `docs/architecture.md`.
Security model: `docs/security.md`.
Per-tool parameter reference: `docs/mcp-tools-reference.md`.
Downstream broker integration: see MotionCatalyst/wpf-mcp separately.
