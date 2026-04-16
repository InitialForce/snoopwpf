namespace SnoopWPF.Agent.Analyzers;

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// SWPF0001 — Reports Console.Write* invocations in assemblies (or types) marked with
/// [McpStdioEntrypoint]. Stdout is claimed by the MCP transport in those projects and any
/// Console.Write* call will corrupt the MCP JSON-RPC stream.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class Console0001Analyzer : DiagnosticAnalyzer
{
    /// <summary>The diagnostic ID for SWPF0001.</summary>
    public const string DiagnosticId = "SWPF0001";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Console.Write* in MCP stdio entrypoint",
        messageFormat: "Console.Write* in MCP stdio entrypoint corrupts the MCP transport stream. Redirect to Trace.TraceInformation or use a log file.",
        category: "SnoopWPF.Agent",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Assemblies and types marked with [McpStdioEntrypoint] own the process stdout for MCP JSON-RPC transport. "
                   + "Any Console.Write* call will corrupt the transport stream. "
                   + "Use System.Diagnostics.Trace.TraceInformation or a log file instead.");

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationCtx =>
        {
            // Check once whether the assembly itself is marked [McpStdioEntrypoint].
            var assemblyMarked = IsAssemblyMarked(compilationCtx.Compilation);

            compilationCtx.RegisterSyntaxNodeAction(
                nodeCtx => AnalyzeInvocation(nodeCtx, assemblyMarked),
                SyntaxKind.InvocationExpression);
        });
    }

    private static bool IsAssemblyMarked(Compilation compilation)
    {
        foreach (var attr in compilation.Assembly.GetAttributes())
        {
            if (IsEntrypointAttribute(attr))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsEntrypointAttribute(AttributeData attr)
    {
        var cls = attr.AttributeClass;
        return cls is not null
            && cls.Name == "McpStdioEntrypointAttribute"
            && cls.ContainingNamespace?.ToDisplayString() == "SnoopWPF.Agent.Contracts";
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext ctx, bool assemblyMarked)
    {
        var invocation = (InvocationExpressionSyntax)ctx.Node;

        // Fast path: only care about member access expressions (Console.Write, Console.WriteLine, etc.)
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return;
        }

        var symbolInfo = ctx.SemanticModel.GetSymbolInfo(invocation, ctx.CancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol method)
        {
            return;
        }

        if (!IsConsoleWriteMethod(method))
        {
            return;
        }

        // If the assembly is already marked, every Console.Write* is a violation.
        if (assemblyMarked)
        {
            ctx.ReportDiagnostic(Diagnostic.Create(Rule, memberAccess.GetLocation()));
            return;
        }

        // Otherwise check if any enclosing type is marked [McpStdioEntrypoint].
        if (IsInsideMarkedType(invocation, ctx.SemanticModel))
        {
            ctx.ReportDiagnostic(Diagnostic.Create(Rule, memberAccess.GetLocation()));
        }
    }

    private static bool IsConsoleWriteMethod(IMethodSymbol method)
    {
        var containingType = method.ContainingType;
        if (containingType is null)
        {
            return false;
        }

        // Check that this is System.Console (not a user-defined type named Console)
        if (containingType.Name != "Console")
        {
            return false;
        }

        if (containingType.ContainingNamespace?.ToDisplayString() != "System")
        {
            return false;
        }

        var name = method.Name;
        return name == "Write" || name == "WriteLine";
    }

    private static bool IsInsideMarkedType(SyntaxNode node, SemanticModel semanticModel)
    {
        // Walk up the syntax tree looking for type declarations.
        var current = node.Parent;
        while (current is not null)
        {
            if (current is TypeDeclarationSyntax typeDecl)
            {
                var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl);
                if (typeSymbol is not null)
                {
                    foreach (var attr in typeSymbol.GetAttributes())
                    {
                        if (IsEntrypointAttribute(attr))
                        {
                            return true;
                        }
                    }
                }
            }

            current = current.Parent;
        }

        return false;
    }
}
