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
/// Tests for SWPF0001 — Console.Write* in [McpStdioEntrypoint]-marked assemblies/types.
/// </summary>
[TestFixture]
public sealed class Console0001AnalyzerTests
{
    // ─── attribute source, injected into every test compilation ─────────────────

    private const string AttributeSource = @"
using System;
namespace SnoopWPF.Agent.Contracts
{
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = false)]
    public sealed class McpStdioEntrypointAttribute : Attribute { }
}
";

    // ─── Test 1: Console.Write in assembly-level [McpStdioEntrypoint] → diagnostic ─

    [Test]
    public async Task AssemblyMarked_ConsoleWrite_ReportsDiagnostic()
    {
        var source = @"
using System;
[assembly: SnoopWPF.Agent.Contracts.McpStdioEntrypointAttribute]
namespace TestApp
{
    public static class Program
    {
        public static void Main()
        {
            Console.Write(""hello"");
        }
    }
}
";
        var diagnostics = await GetDiagnosticsAsync(source);

        Assert.That(diagnostics, Has.Count.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("SWPF0001"));
        Assert.That(diagnostics[0].GetMessage(), Does.Contain("corrupts the MCP transport stream"));
    }

    // ─── Test 2: Console.Write in type-level [McpStdioEntrypoint] → diagnostic ──

    [Test]
    public async Task TypeMarked_ConsoleWriteLine_ReportsDiagnostic()
    {
        var source = @"
using System;
using SnoopWPF.Agent.Contracts;
namespace TestApp
{
    [McpStdioEntrypoint]
    public static class Host
    {
        public static void Run()
        {
            Console.WriteLine(""starting"");
        }
    }
}
";
        var diagnostics = await GetDiagnosticsAsync(source);

        Assert.That(diagnostics, Has.Count.EqualTo(1));
        Assert.That(diagnostics[0].Id, Is.EqualTo("SWPF0001"));
    }

    // ─── Test 3: Console.Write in non-marked code → no diagnostic ───────────────

    [Test]
    public async Task UnmarkedCode_ConsoleWrite_NoDiagnostic()
    {
        var source = @"
using System;
namespace TestApp
{
    public static class Program
    {
        public static void Main()
        {
            Console.WriteLine(""this is fine"");
        }
    }
}
";
        var diagnostics = await GetDiagnosticsAsync(source);

        Assert.That(diagnostics, Is.Empty,
            "No diagnostic should fire when [McpStdioEntrypoint] is absent.");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────

    private static async Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(
        string source,
        CancellationToken cancellationToken = default)
    {
        var compilation = CreateCompilation(source);
        var analyzer = new Console0001Analyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));

        var allDiagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(cancellationToken);
        return allDiagnostics
            .Where(d => d.Id == Console0001Analyzer.DiagnosticId)
            .ToList();
    }

    private static Compilation CreateCompilation(string source)
    {
        var syntaxTrees = new[]
        {
            CSharpSyntaxTree.ParseText(SourceText.From(AttributeSource)),
            CSharpSyntaxTree.ParseText(SourceText.From(source)),
        };

        var references = BuildReferences();

        return CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: syntaxTrees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    internal static MetadataReference[] BuildReferences()
    {
        var refs = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Console).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Attribute).Assembly.Location),
        };

        var runtimePath = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!,
            "System.Runtime.dll");
        if (System.IO.File.Exists(runtimePath))
        {
            refs.Add(MetadataReference.CreateFromFile(runtimePath));
        }

        return refs.ToArray();
    }
}
