# Security Policy

## Scope

This policy applies to the **InitialForce/snoopwpf** fork and the MCP agent surface
(`SnoopWPF.Agent`, `snoop-mcp`, NuGet mode).

---

## Supported Versions

| Branch / Tag | Supported |
|---|---|
| `develop` (latest) | Yes |
| Latest tagged release | Yes |
| Older tags | No — please upgrade |

---

## Reporting a Vulnerability

**Do not open a public GitHub issue for security vulnerabilities.**

Use [GitHub security advisories](https://github.com/InitialForce/snoopwpf/security/advisories/new)
to report vulnerabilities privately. GitHub Security Advisories provide end-to-end
encrypted communication and coordinate CVE assignment when appropriate.

### Response timeline

| Event | Target |
|---|---|
| Acknowledgement | 3 business days |
| Triage / severity assessment | 7 business days |
| Fix or mitigation shipped | 30 business days for high/critical; 60 for medium/low |
| Public disclosure | Coordinated with reporter after fix ships |

---

## Cryptographic Primitives Shipped

The following security controls are present in the current codebase:

| Primitive | Location | Purpose |
|---|---|---|
| `PipeOptions.CurrentUserOnly` | `PipeConnection` (injection mode), `McpServerSetup.RunWithPipeAsync` (NuGet mode) | Restricts named pipe ACL to the current Windows user |
| `RandomNumberGenerator.GetBytes(32)` | Session token generation | 256-bit cryptographically random session token |
| `CryptographicOperations.FixedTimeEquals` | Pipe handshake token verification | Constant-time comparison to prevent timing oracle attacks |
| HMAC-SHA256 audit chain | Planned for v1.1 | Tamper-evident log of MCP tool invocations |

The session token is zeroed from memory immediately after the handshake completes
and is never written to logs or disk.

---

## Known Non-Goals (MVP)

The following are explicitly out of scope for the current release:

- **No TLS.** The MCP server communicates over `127.0.0.1` named pipes (injection
  mode) or stdio (NuGet mode). Localhost-only transport is the only network boundary.
  TLS is not needed and is not planned for local use.
- **No remote MCP.** The server never binds to a non-loopback address. Remote
  agent access is not a supported configuration.
- **No multi-user authentication model.** Authentication is limited to the
  256-bit session token handshake described above. Role-based access control
  and multi-user sessions are not implemented.
- **No `SnoopLog.txt` ACL or rotation.** See the security model document
  (`docs/security.md`) for details. ACL + rotation is tracked for v1.1.

---

## Further Reading

- [`docs/security.md`](docs/security.md) — full threat model and control descriptions.
