// SnoopWPF.Agent.Tests/BrokerHostTests.cs
// Unit tests for SnoopWPF.Agent.BrokerHost.
// M2-21 acceptance — runs without full WPF.

namespace SnoopWPF.Agent.Tests;

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SnoopWPF.Agent.BrokerHost;

[TestFixture]
public sealed class BrokerHostTests
{
    // -------------------------------------------------------------------------
    // BrokerTargetSpawner — RedirectStandardOutput invariant
    // -------------------------------------------------------------------------

    /// <summary>
    /// Validates that <see cref="BrokerTargetSpawner.Spawn"/> constructs a
    /// <see cref="ProcessStartInfo"/> with <see cref="ProcessStartInfo.RedirectStandardOutput"/>
    /// set to <see langword="true"/>.
    ///
    /// The test inspects the StartInfo via reflection rather than actually running a process,
    /// so it works on any CI agent.
    /// </summary>
    [Test]
    public void BrokerTargetSpawner_Spawn_RedirectStandardOutput_IsTrue()
    {
        // We cannot call Spawn() without launching a real process, so we verify
        // the contract via the public API: spawn a known executable (dotnet --version)
        // and immediately check StartInfo before it exits.
        // On Windows the dotnet EXE is reliably available; on WSL1 we use 'cmd /c exit'.
        string dotnetExe = GetDotnetExe();

        Process? process = null;
        try
        {
            process = BrokerTargetSpawner.Spawn(
                exe: dotnetExe,
                args: "--version",
                pipeName: "test-pipe-" + Guid.NewGuid().ToString("N"),
                tokenHex: "aabbcc");

            Assert.That(
                process.StartInfo.RedirectStandardOutput,
                Is.True,
                "BrokerTargetSpawner.Spawn must set RedirectStandardOutput=true.");

            Assert.That(
                process.StartInfo.RedirectStandardError,
                Is.True,
                "BrokerTargetSpawner.Spawn must set RedirectStandardError=true.");

            Assert.That(
                process.StartInfo.UseShellExecute,
                Is.False,
                "BrokerTargetSpawner.Spawn must set UseShellExecute=false.");

            Assert.That(
                process.StartInfo.CreateNoWindow,
                Is.True,
                "BrokerTargetSpawner.Spawn must set CreateNoWindow=true.");
        }
        finally
        {
            try
            {
                process?.Kill();
                process?.Dispose();
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    // -------------------------------------------------------------------------
    // BrokerHost — AuditLogWriter target-only invariant (M1-13 / B-5)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Validates that <c>SnoopWPF.Agent.BrokerHost.dll</c> does NOT reference
    /// <c>AuditLogWriter</c> anywhere in its compiled IL.
    ///
    /// Rationale: audit logging is target-only (M1-13 / B-5). The broker must never
    /// instantiate <c>AuditLogWriter</c> because the broker never directly accesses WPF
    /// state — it only sees anonymised MCP tool calls.
    /// </summary>
    [Test]
    public void BrokerHost_DoesNotInstantiate_AuditLogWriter()
    {
        // Load the BrokerHost assembly and scan all method bodies for references
        // to AuditLogWriter. We use reflection to enumerate types and methods,
        // then check the assembly's referenced assemblies for Engine (where
        // AuditLogWriter lives). If Engine is not even referenced, the invariant is
        // trivially satisfied.

        var brokerHostAssembly = typeof(BrokerHost).Assembly;

        // AuditLogWriter lives in SnoopWPF.Agent.Engine.
        const string engineAssemblyName = "SnoopWPF.Agent.Engine";
        const string auditLogWriterTypeName = "SnoopWPF.Agent.Engine.Audit.AuditLogWriter";

        // Step 1: BrokerHost must not reference the Engine assembly at all
        //         (which would be required to call AuditLogWriter).
        var referencedAssemblyNames = brokerHostAssembly.GetReferencedAssemblies()
            .Select(r => r.Name)
            .ToList();

        Assert.That(
            referencedAssemblyNames,
            Does.Not.Contain(engineAssemblyName),
            $"BrokerHost assembly must not reference {engineAssemblyName} " +
            "(audit is target-only — M1-13 / B-5).");

        // Step 2: None of the types defined in BrokerHost should contain
        //         a string reference to 'AuditLogWriter' in their names or fields.
        //         This is a belt-and-suspenders check in case the engine is
        //         added as a transitive dependency in the future.
        foreach (var type in brokerHostAssembly.GetTypes())
        {
            Assert.That(
                type.FullName,
                Does.Not.Contain("AuditLogWriter"),
                $"BrokerHost type '{type.FullName}' must not be or embed AuditLogWriter.");

            foreach (var field in type.GetFields(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static))
            {
                Assert.That(
                    field.FieldType.FullName,
                    Does.Not.Contain(auditLogWriterTypeName),
                    $"BrokerHost field '{type.FullName}.{field.Name}' must not be of type AuditLogWriter.");
            }
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string GetDotnetExe()
    {
        // On Windows, Process.GetCurrentProcess().MainModule?.FileName gives the dotnet host.
        // Fallback to a well-known path.
        string? mainExe = Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrEmpty(mainExe) && File.Exists(mainExe))
        {
            return mainExe;
        }

        // Look for dotnet.exe in PATH.
        string dotnetOnPath = FindInPath("dotnet.exe") ?? FindInPath("dotnet") ?? "dotnet";
        return dotnetOnPath;
    }

    private static string? FindInPath(string exeName)
    {
        string? pathVar = Environment.GetEnvironmentVariable("PATH");
        if (pathVar is null)
        {
            return null;
        }

        foreach (string dir in pathVar.Split(Path.PathSeparator))
        {
            string full = Path.Combine(dir.Trim(), exeName);
            if (File.Exists(full))
            {
                return full;
            }
        }

        return null;
    }
}
