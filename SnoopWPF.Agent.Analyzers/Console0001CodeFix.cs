namespace SnoopWPF.Agent.Analyzers;

using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
/// Code fix for SWPF0001 — rewrites Console.Write[Line](...) to
/// System.Diagnostics.Trace.TraceInformation(...).
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(Console0001CodeFix))]
[Shared]
public sealed class Console0001CodeFix : CodeFixProvider
{
    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(Console0001Analyzer.DiagnosticId);

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        var diagnostic = context.Diagnostics[0];
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        // The diagnostic location is on the member access expression.
        var memberAccess = root.FindNode(diagnosticSpan) as MemberAccessExpressionSyntax;
        if (memberAccess is null)
        {
            return;
        }

        var invocation = memberAccess.Parent as InvocationExpressionSyntax;
        if (invocation is null)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Replace with Trace.TraceInformation",
                createChangedDocument: ct => ReplaceWithTraceAsync(context.Document, invocation, ct),
                equivalenceKey: "ReplaceConsoleWriteWithTraceInformation"),
            diagnostic);
    }

    private static async Task<Document> ReplaceWithTraceAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        // Build System.Diagnostics.Trace.TraceInformation(...)
        // We use the fully-qualified form to avoid needing a using directive.
        var traceExpression = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName("System"),
                    SyntaxFactory.IdentifierName("Diagnostics")),
                SyntaxFactory.IdentifierName("Trace")),
            SyntaxFactory.IdentifierName("TraceInformation"));

        // Trace.TraceInformation accepts a string format + params object[].
        // We pass the original arguments through as-is — best-effort rewrite.
        var newInvocation = invocation
            .WithExpression(traceExpression)
            .WithTriviaFrom(invocation);

        var newRoot = root.ReplaceNode(invocation, newInvocation);
        return document.WithSyntaxRoot(newRoot);
    }
}
