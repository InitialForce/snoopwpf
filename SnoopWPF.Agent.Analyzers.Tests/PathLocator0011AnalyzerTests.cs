namespace SnoopWPF.Agent.Analyzers.Tests;

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using SnoopWPF.Agent.Analyzers;

/// <summary>
/// Tests for SWPF0011 — Stored path= locator warning.
/// </summary>
[TestFixture]
public sealed class PathLocator0011AnalyzerTests
{
    // ─── Test 1: field assignment with path= literal → diagnostic ───────────────

    [Test]
    public async Task FieldAssignment_PathLocatorLiteral_ReportsDiagnostic()
    {
        var source = @"
namespace Consumer
{
    public class MyTool
    {
        private string _locator;

        public void Store()
        {
            _locator = ""path=Window\\Grid"";
        }
    }
}
";
        var diagnostics = await GetDiagnosticsAsync(source);

        Assert.That(diagnostics, Has.Count.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("SWPF0011"));
        Assert.That(diagnostics[0].GetMessage(), Does.Contain("path=Window"));
        Assert.That(diagnostics[0].GetMessage(), Does.Contain("_locator"));
    }

    // ─── Test 2: local variable with path= literal → no diagnostic ──────────────

    [Test]
    public async Task LocalVariable_PathLocatorLiteral_NoDiagnostic()
    {
        var source = @"
namespace Consumer
{
    public class MyTool
    {
        public void UseLocally()
        {
            string locator = ""path=Window\\Grid"";
            _ = locator.Length; // consumed immediately, not stored
        }
    }
}
";
        var diagnostics = await GetDiagnosticsAsync(source);

        Assert.That(diagnostics, Is.Empty,
            "Local variable assignment of a path= literal should not trigger SWPF0011.");
    }

    // ─── Test 3: non-path= string stored in a field → no diagnostic ─────────────

    [Test]
    public async Task FieldAssignment_NonPathLocatorString_NoDiagnostic()
    {
        var source = @"
namespace Consumer
{
    public class MyTool
    {
        private string _name;

        public void SetName()
        {
            _name = ""automationId=SubmitButton"";
        }
    }
}
";
        var diagnostics = await GetDiagnosticsAsync(source);

        Assert.That(diagnostics, Is.Empty,
            "Storing a non-path= locator string in a field must not trigger SWPF0011.");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────

    private static async Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(
        string source,
        CancellationToken cancellationToken = default)
    {
        var compilation = CreateCompilation(source);
        var analyzer = new PathLocator0011Analyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));

        var allDiagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(cancellationToken);
        return allDiagnostics
            .Where(d => d.Id == PathLocator0011Analyzer.DiagnosticId)
            .ToList();
    }

    private static Compilation CreateCompilation(string source)
    {
        var syntaxTrees = new[]
        {
            CSharpSyntaxTree.ParseText(SourceText.From(source)),
        };

        var references = Console0001AnalyzerTests.BuildReferences();

        return CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: syntaxTrees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
