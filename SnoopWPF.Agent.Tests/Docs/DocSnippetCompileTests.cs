// SnoopWPF.Agent.Tests/Docs/DocSnippetCompileTests.cs
// FX6-H3: compile-time guard for doc quickstart snippets.
// These tests do NOT run any logic — they exist purely so the compiler catches
// doc snippet API drift (wrong ctor signature, renamed types, etc.).

namespace SnoopWPF.Agent.Tests.Docs;

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NUnit.Framework;
using SnoopWPF.Agent.BrokerHost;

/// <summary>
/// Compile-time guard for broker-mode quickstart snippet in
/// <c>docs/brokered-mode-integration.md</c>.
/// </summary>
/// <remarks>
/// FX6-H3: The doc previously used <c>new StdioServerTransport()</c> (zero-arg ctor
/// that does not exist in MCP 1.2.0). This test fixture contains the verbatim snippet
/// as compilable C# so that any future ctor-signature drift is caught at build time.
/// Tests are no-ops at runtime — they exist to satisfy the compiler.
/// </remarks>
[TestFixture]
public sealed class DocSnippetCompileTests
{
    /// <summary>
    /// Verifies that the broker-side quickstart snippet compiles against the actual
    /// MCP 1.2.0 API. The method body is NEVER executed — compilation is the assertion.
    /// </summary>
    [Test]
    [Explicit("Compile-time guard only — do not execute; method returns immediately.")]
    public void BrokeredQuickstart_Compiles()
    {
        // --- snippet begin (mirrors docs/brokered-mode-integration.md broker skeleton) ---
        // This code is intentionally unreachable; we return immediately to avoid
        // side effects (spawning processes, redirecting stdout, etc.).
        return;

#pragma warning disable CS0162 // Unreachable code detected — intentional: compile-time guard
        string pipeName = "my-app-" + Guid.NewGuid().ToString("N")[..8];
        string tokenHex = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        using var cts = new CancellationTokenSource();

        // 2. Silence stdout.
        Console.SetOut(TextWriter.Null);

        // 3. Spawn target — IReadOnlyList<string> overload (non-deprecated).
        var process = BrokerTargetSpawner.Spawn(
            exe: @"C:\path\to\MyApp.exe",
            args: Array.Empty<string>(),
            pipeName: pipeName,
            tokenHex: tokenHex);

        // 4. Build options.
        var opts = new BrokerOptions
        {
            PipeName = pipeName,
            SessionToken = tokenHex,
            OnTargetDisconnected = () => cts.Cancel(),
        };

        // 5. StdioServerTransport(McpServerOptions) — MCP 1.2.0 API.
        var serverOptions = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "my-broker", Version = "1.0.0" },
        };

        // The following line is the critical compile check: zero-arg ctor does not exist.
        _ = new StdioServerTransport(serverOptions);

        // Cleanup reference to avoid CS0168 "variable declared but never used"
        _ = (process, opts);
#pragma warning restore CS0162
    }
}
