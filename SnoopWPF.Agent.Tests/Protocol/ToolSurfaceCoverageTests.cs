namespace SnoopWPF.Agent.Tests.Protocol;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ModelContextProtocol.Server;
using NUnit.Framework;
using SnoopWPF.Agent.Tools;

/// <summary>
/// Regression guard (FX6-I2): asserts that every <c>[McpServerToolType]</c> tool class in
/// <c>SnoopWPF.Agent.Tools</c> has a corresponding <c>*Tests.cs</c> fixture file in
/// <c>SnoopWPF.Agent.Tests/Tools/</c>. Fails with a list of uncovered tools so coverage
/// gaps are caught immediately when new tools are added.
/// </summary>
[TestFixture]
public class ToolSurfaceCoverageTests
{
    /// <summary>
    /// Walks all <c>[McpServerToolType]</c> types in the tools assembly and asserts that a
    /// matching <c>&lt;ClassName&gt;Tests.cs</c> file exists in the tests project's Tools folder.
    /// </summary>
    [Test]
    public void EveryToolHasUnitTests()
    {
        // ── 1. Discover all tool types via reflection ───────────────────────────
        var toolAssembly = typeof(ClickTool).Assembly;

        var toolTypes = toolAssembly
            .GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .Where(t => t.IsClass && !t.IsAbstract)
            .Select(t => t.Name)
            .OrderBy(n => n)
            .ToList();

        Assert.That(toolTypes, Is.Not.Empty, "Expected to find at least one [McpServerToolType] class.");

        // ── 2. Locate the Tools test directory ─────────────────────────────────
        // Walk up from the assembly location to find the repo root, then the test dir.
        var testToolsDir = FindTestToolsDirectory();

        // If the directory doesn't exist in the current environment (e.g. CI running
        // from a publish-only drop), skip gracefully — coverage is only meaningful
        // when source is present.
        if (testToolsDir is null || !Directory.Exists(testToolsDir))
        {
            Assert.Inconclusive(
                $"Could not locate SnoopWPF.Agent.Tests/Tools/ directory from '{AppContext.BaseDirectory}'. " +
                "Skipping surface coverage check.");
            return;
        }

        // ── 3. Collect all existing *Tests.cs files (by class name prefix) ─────
        var existingFixtures = Directory
            .GetFiles(testToolsDir, "*Tests.cs", SearchOption.TopDirectoryOnly)
            .Select(f => Path.GetFileNameWithoutExtension(f)) // e.g. "ClickToolTests"
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // ── 4. Check each tool has a corresponding fixture ─────────────────────
        var missing = new List<string>();

        foreach (var toolName in toolTypes)
        {
            var expectedFixture = $"{toolName}Tests"; // e.g. "ClickToolTests"
            if (!existingFixtures.Contains(expectedFixture))
            {
                missing.Add(toolName);
            }
        }

        if (missing.Count > 0)
        {
            var list = string.Join("\n  - ", missing);
            Assert.Fail(
                $"The following tools are missing a unit-test fixture in SnoopWPF.Agent.Tests/Tools/:\n" +
                $"  - {list}\n\n" +
                $"Create a <ToolName>Tests.cs file for each missing tool (see SetSliderValueToolTests.cs for template).");
        }
    }

    /// <summary>
    /// Regression guard (FX6-C4): asserts that the number of registered <c>[McpServerToolType]</c>
    /// classes matches the PRD-documented tool count.  Fails immediately when a new tool is
    /// added without updating the PRD, or when a tool is removed without bumping the count.
    /// </summary>
    [Test]
    public void ToolCount_MatchesPrd()
    {
        // PRD §5 + Addendum M4 (FX6-C4): 29 tools + 4 WS3 tools (bd-1a9.39/40/41/42) + 1 LLM-nav tool (wpf_get_actionables) = 34.
        // Update this constant (and PRD.md + docs/prd-scope-addendum-m4.md + docs/mcp-tools-reference.md)
        // whenever a tool is added or removed.
        const int PrdToolCount = 34;

        var toolAssembly = typeof(ClickTool).Assembly;

        var toolMethods = toolAssembly
            .GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .Where(t => t.IsClass && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .ToList();

        Assert.That(
            toolMethods.Count,
            Is.EqualTo(PrdToolCount),
            $"Expected {PrdToolCount} registered MCP tools (per PRD §5 + Addendum M4 FX6-C4), " +
            $"but found {toolMethods.Count}. " +
            "Update PrdToolCount, PRD.md, docs/prd-scope-addendum-m4.md, and docs/mcp-tools-reference.md.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string? FindTestToolsDirectory()
    {
        // The assembly runs from a bin/ output directory; walk upward to find the
        // repo root (identified by Snoop.sln), then build the expected path.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            if (dir.GetFiles("Snoop.sln").Length > 0)
            {
                // Found repo root — construct the expected test tools path.
                return Path.Combine(dir.FullName, "SnoopWPF.Agent.Tests", "Tools");
            }

            dir = dir.Parent;
        }

        return null;
    }
}
