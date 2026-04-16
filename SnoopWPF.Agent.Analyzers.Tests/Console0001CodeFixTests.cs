namespace SnoopWPF.Agent.Analyzers.Tests;

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using SnoopWPF.Agent.Analyzers;

/// <summary>
/// Tests for the SWPF0001 code fix — Console.Write* → Trace.TraceInformation.
/// </summary>
[TestFixture]
public sealed class Console0001CodeFixTests
{
    private const string AttributeSource = @"
using System;
namespace SnoopWPF.Agent.Contracts
{
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class, AllowMultiple = false)]
    public sealed class McpStdioEntrypointAttribute : Attribute { }
}
";

    /// <summary>
    /// Code fix rewrites Console.Write(str) to System.Diagnostics.Trace.TraceInformation(str).
    /// </summary>
    [Test]
    public async Task CodeFix_ConsoleWrite_RewritesToTraceTraceInformation()
    {
        const string source = @"
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
        var fixedSource = await ApplyCodeFixAsync(source);

        Assert.That(fixedSource, Does.Contain("Trace.TraceInformation"),
            "Code fix should have replaced Console.Write with System.Diagnostics.Trace.TraceInformation");
        Assert.That(fixedSource, Does.Not.Contain("Console.Write"),
            "Console.Write should have been removed by the code fix");
    }

    // ─── Helper ──────────────────────────────────────────────────────────────────

    private static async Task<string> ApplyCodeFixAsync(
        string source,
        CancellationToken cancellationToken = default)
    {
        var allRefs = Console0001AnalyzerTests.BuildReferences();

        var attributeTree = CSharpSyntaxTree.ParseText(SourceText.From(AttributeSource));
        var sourceTree = CSharpSyntaxTree.ParseText(SourceText.From(source));

        var compilation = CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: new[] { attributeTree, sourceTree },
            references: allRefs,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new Console0001Analyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));

        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(cancellationToken);
        var swpf0001 = diagnostics.Where(d => d.Id == Console0001Analyzer.DiagnosticId).ToList();
        Assert.That(swpf0001, Has.Count.GreaterThan(0), "Expected at least one SWPF0001 diagnostic to apply fix to.");

        // Build a workspace and apply the code fix.
        var workspace = new Microsoft.CodeAnalysis.AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var documentId1 = DocumentId.CreateNewId(projectId);
        var documentId2 = DocumentId.CreateNewId(projectId);

        var solution = workspace.CurrentSolution
            .AddProject(projectId, "TestProject", "TestProject", LanguageNames.CSharp)
            .WithProjectCompilationOptions(projectId, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddMetadataReferences(projectId, allRefs)
            .AddDocument(documentId1, "Attribute.cs", AttributeSource)
            .AddDocument(documentId2, "Source.cs", source);

        var document = solution.GetDocument(documentId2)!;
        var project = solution.GetProject(projectId)!;
        var workspaceCompilation = await project.GetCompilationAsync(cancellationToken);
        Assert.That(workspaceCompilation, Is.Not.Null);

        var workspaceWithAnalyzers = workspaceCompilation!.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));

        var workspaceDiagnostics = await workspaceWithAnalyzers.GetAnalyzerDiagnosticsAsync(cancellationToken);
        var firstDiag = workspaceDiagnostics.FirstOrDefault(d => d.Id == Console0001Analyzer.DiagnosticId);
        Assert.That(firstDiag, Is.Not.Null, "Diagnostic not found in workspace compilation.");

        // Apply code fix
        var codeFix = new Console0001CodeFix();
        var actions = new List<Microsoft.CodeAnalysis.CodeActions.CodeAction>();
        var fixContext = new CodeFixContext(
            document,
            firstDiag!,
            (action, _) => actions.Add(action),
            cancellationToken);

        await codeFix.RegisterCodeFixesAsync(fixContext);
        Assert.That(actions, Has.Count.GreaterThan(0), "Code fix should provide at least one action.");

        var operations = await actions[0].GetOperationsAsync(cancellationToken);
        var applyOperation = operations.OfType<Microsoft.CodeAnalysis.CodeActions.ApplyChangesOperation>().First();
        var changedSolution = applyOperation.ChangedSolution;
        var changedDocument = changedSolution.GetDocument(documentId2)!;
        var changedRoot = await changedDocument.GetSyntaxRootAsync(cancellationToken);
        return changedRoot!.ToFullString();
    }
}
