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
/// Tests for SWPF0010 — Stored nodeId warning.
/// </summary>
[TestFixture]
public sealed class NodeId0010AnalyzerTests
{
    // ─── Shared stub for a nodeId-producing service ──────────────────────────────

    private const string StubSource = @"
namespace SnoopWPF.Agent
{
    public class SnoopService
    {
        public string GetNodeId(string path) => path;
        public string GetElementNodeId(int index) => index.ToString();
    }
}
";

    // ─── Test 1: field assignment from GetNodeId → diagnostic ───────────────────

    [Test]
    public async Task FieldAssignment_FromNodeIdMethod_ReportsDiagnostic()
    {
        var source = @"
using SnoopWPF.Agent;

namespace Consumer
{
    public class MyTool
    {
        private string _storedId;

        private readonly SnoopService _svc = new SnoopService();

        public void Cache()
        {
            _storedId = _svc.GetNodeId(""root"");
        }
    }
}
";
        var diagnostics = await GetDiagnosticsAsync(source);

        Assert.That(diagnostics, Has.Count.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("SWPF0010"));
        Assert.That(diagnostics[0].GetMessage(), Does.Contain("GetNodeId"));
        Assert.That(diagnostics[0].GetMessage(), Does.Contain("_storedId"));
    }

    // ─── Test 2: local variable assignment → no diagnostic ──────────────────────

    [Test]
    public async Task LocalVariable_FromNodeIdMethod_NoDiagnostic()
    {
        var source = @"
using SnoopWPF.Agent;

namespace Consumer
{
    public class MyTool
    {
        private readonly SnoopService _svc = new SnoopService();

        public void UseLocally()
        {
            string localId = _svc.GetNodeId(""root"");
            _ = localId.Length; // consumed immediately
        }
    }
}
";
        var diagnostics = await GetDiagnosticsAsync(source);

        Assert.That(diagnostics, Is.Empty,
            "Local variable assignment of a nodeId should not trigger SWPF0010.");
    }

    // ─── Test 3: non-nodeId string field → no diagnostic ────────────────────────

    [Test]
    public async Task FieldAssignment_NonNodeIdString_NoDiagnostic()
    {
        var source = @"
namespace Consumer
{
    public class MyTool
    {
        private string _name;

        public void SetName(string value)
        {
            _name = value;
        }
    }
}
";
        var diagnostics = await GetDiagnosticsAsync(source);

        Assert.That(diagnostics, Is.Empty,
            "Assigning an arbitrary string to a field must not trigger SWPF0010.");
    }

    // ─── Test 4: list.Add(GetNodeId(...)) on a field → diagnostic ────────────────

    [Test]
    public async Task ListAdd_NodeIdOnField_ReportsDiagnostic()
    {
        var source = @"
using System.Collections.Generic;
using SnoopWPF.Agent;

namespace Consumer
{
    public class MyTool
    {
        private readonly List<string> _ids = new List<string>();
        private readonly SnoopService _svc = new SnoopService();

        public void Collect()
        {
            _ids.Add(_svc.GetNodeId(""root""));
        }
    }
}
";
        var diagnostics = await GetDiagnosticsAsync(source);

        Assert.That(diagnostics, Has.Count.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("SWPF0010"));
    }

    // ─── Test 5: dictionary element access assignment → diagnostic ───────────────

    [Test]
    public async Task DictionaryElementAssignment_NodeId_ReportsDiagnostic()
    {
        var source = @"
using System.Collections.Generic;
using SnoopWPF.Agent;

namespace Consumer
{
    public class MyTool
    {
        private readonly Dictionary<string, string> _map = new Dictionary<string, string>();
        private readonly SnoopService _svc = new SnoopService();

        public void Index()
        {
            _map[""root""] = _svc.GetElementNodeId(0);
        }
    }
}
";
        var diagnostics = await GetDiagnosticsAsync(source);

        Assert.That(diagnostics, Has.Count.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("SWPF0010"));
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────

    private static async Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(
        string source,
        CancellationToken cancellationToken = default)
    {
        var compilation = CreateCompilation(source);
        var analyzer = new NodeId0010Analyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));

        var allDiagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(cancellationToken);
        return allDiagnostics
            .Where(d => d.Id == NodeId0010Analyzer.DiagnosticId)
            .ToList();
    }

    private static Compilation CreateCompilation(string source)
    {
        var syntaxTrees = new[]
        {
            CSharpSyntaxTree.ParseText(SourceText.From(StubSource)),
            CSharpSyntaxTree.ParseText(SourceText.From(source)),
        };

        var references = Console0001AnalyzerTests.BuildReferences();

        // Add System.Collections.Generic reference
        var refList = new List<MetadataReference>(references)
        {
            MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Dictionary<,>).Assembly.Location),
        };

        // Add netstandard / collections runtime dlls if available
        var collectionsPath = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!,
            "System.Collections.dll");
        if (System.IO.File.Exists(collectionsPath))
        {
            refList.Add(MetadataReference.CreateFromFile(collectionsPath));
        }

        return CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: syntaxTrees,
            references: refList.ToArray(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
