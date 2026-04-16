namespace SnoopWPF.Agent.Analyzers;

using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// SWPF0010 — Warns when a <c>string</c> value returned from a known nodeId-producing API
/// is stored in a field, property, collection, or dictionary.
///
/// NodeIds are session-scoped identifiers issued by the SnoopWPF MCP server for a specific
/// WPF element in a single inspection session. Persisting a nodeId across sessions (by
/// stashing it in a field, list, or dictionary) will silently break on reconnect because
/// the server issues fresh IDs after each restart. Callers should store a <c>WpfLocator</c>
/// instead, which encodes a stable structural path that survives reconnects.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NodeId0010Analyzer : DiagnosticAnalyzer
{
    /// <summary>The diagnostic ID for SWPF0010.</summary>
    public const string DiagnosticId = "SWPF0010";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Stored nodeId — use WpfLocator instead",
        messageFormat: "'{0}' returns a session-scoped nodeId; storing it in '{1}' will silently break on reconnect. Store a WpfLocator instead.",
        category: "SnoopWPF.Agent",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "NodeIds are session-scoped identifiers that become invalid after a server reconnect. "
                   + "Persisting them in fields, properties, collections, or dictionaries will cause silent "
                   + "failures on subsequent tool calls. Store a WpfLocator (structural path) instead.");

    // Known method names whose return values are nodeIds.
    // The containing type is also checked where practical, but for flexibility we keep
    // a name-only list so it works against generated proxies and interface implementations.
    private static readonly ImmutableHashSet<string> NodeIdMethodNames = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "GetNodeId",
        "FindNodeId",
        "ResolveNodeId",
        "GetRootNodeId",
        "GetElementNodeId",
        "SnapshotNodeId");

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Assignment expressions: field = GetNodeId(...), _dict[key] = GetNodeId(...)
        context.RegisterSyntaxNodeAction(AnalyzeAssignment, SyntaxKind.SimpleAssignmentExpression);

        // Object initializers in field/property declarations: string _id = GetNodeId(...)
        context.RegisterSyntaxNodeAction(AnalyzeFieldDeclaration, SyntaxKind.FieldDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzePropertyDeclaration, SyntaxKind.PropertyDeclaration);

        // Collection/dictionary mutation: list.Add(GetNodeId(...)), dict.Add(key, GetNodeId(...))
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    // ─── Syntax-node handlers ────────────────────────────────────────────────────

    private static void AnalyzeAssignment(SyntaxNodeAnalysisContext ctx)
    {
        var assignment = (AssignmentExpressionSyntax)ctx.Node;

        if (!IsNodeIdExpression(assignment.Right, ctx.SemanticModel, ctx.CancellationToken,
                out var producerName))
        {
            return;
        }

        // Determine what the left-hand side is: field, property, or element access (dict/list)
        var lhs = assignment.Left;
        var targetDescription = GetLhsDescription(lhs, ctx.SemanticModel, ctx.CancellationToken);
        if (targetDescription is null)
        {
            return; // local variable — not a persistent storage location
        }

        ctx.ReportDiagnostic(Diagnostic.Create(Rule, assignment.GetLocation(),
            producerName, targetDescription));
    }

    private static void AnalyzeFieldDeclaration(SyntaxNodeAnalysisContext ctx)
    {
        var fieldDecl = (FieldDeclarationSyntax)ctx.Node;

        foreach (var variable in fieldDecl.Declaration.Variables)
        {
            if (variable.Initializer is null)
            {
                continue;
            }

            if (IsNodeIdExpression(variable.Initializer.Value, ctx.SemanticModel,
                    ctx.CancellationToken, out var producerName))
            {
                ctx.ReportDiagnostic(Diagnostic.Create(Rule, variable.GetLocation(),
                    producerName, variable.Identifier.Text));
            }
        }
    }

    private static void AnalyzePropertyDeclaration(SyntaxNodeAnalysisContext ctx)
    {
        var propDecl = (PropertyDeclarationSyntax)ctx.Node;

        // Auto-property initializer: public string Id { get; } = GetNodeId(...)
        if (propDecl.Initializer is not null &&
            IsNodeIdExpression(propDecl.Initializer.Value, ctx.SemanticModel,
                ctx.CancellationToken, out var producerName))
        {
            ctx.ReportDiagnostic(Diagnostic.Create(Rule, propDecl.Initializer.GetLocation(),
                producerName, propDecl.Identifier.Text));
        }
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext ctx)
    {
        var invocation = (InvocationExpressionSyntax)ctx.Node;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
        {
            return;
        }

        var methodName = memberAccess.Name.Identifier.Text;

        // Only care about collection mutation methods
        if (methodName != "Add" && methodName != "TryAdd" && methodName != "Append")
        {
            return;
        }

        var args = invocation.ArgumentList.Arguments;
        if (args.Count == 0)
        {
            return;
        }

        // For Add/TryAdd on a dictionary: (key, value) — nodeId typically in last arg.
        // For list.Add / list.Append: only one argument.
        var nodeIdArg = args[args.Count - 1].Expression;

        if (!IsNodeIdExpression(nodeIdArg, ctx.SemanticModel, ctx.CancellationToken,
                out var producerName))
        {
            return;
        }

        // Verify the receiver is a field/property (i.e. persistent storage)
        var receiverSymbol = ctx.SemanticModel.GetSymbolInfo(memberAccess.Expression,
            ctx.CancellationToken).Symbol;

        if (receiverSymbol is IFieldSymbol or IPropertySymbol)
        {
            ctx.ReportDiagnostic(Diagnostic.Create(Rule, nodeIdArg.GetLocation(),
                producerName, memberAccess.Expression.ToString()));
        }
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> when <paramref name="expr"/> is an invocation of a known
    /// nodeId-producing method that returns <c>string</c>.  Sets
    /// <paramref name="producerName"/> to the invoked method name.
    /// </summary>
    private static bool IsNodeIdExpression(
        ExpressionSyntax expr,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken,
        out string producerName)
    {
        producerName = string.Empty;

        // Unwrap await expressions: await GetNodeIdAsync(...)
        if (expr is AwaitExpressionSyntax awaitExpr)
        {
            expr = awaitExpr.Expression;
        }

        if (expr is not InvocationExpressionSyntax invocation)
        {
            return false;
        }

        // Resolve the called method symbol
        var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol method)
        {
            return false;
        }

        if (!NodeIdMethodNames.Contains(method.Name))
        {
            return false;
        }

        // The return type must be string (or Task<string> / ValueTask<string>)
        if (!ReturnsString(method))
        {
            return false;
        }

        producerName = method.Name;
        return true;
    }

    private static bool ReturnsString(IMethodSymbol method)
    {
        var returnType = method.ReturnType;

        if (returnType.SpecialType == SpecialType.System_String)
        {
            return true;
        }

        // Task<string> / ValueTask<string>
        if (returnType is INamedTypeSymbol { IsGenericType: true } named)
        {
            var typeName = named.ConstructedFrom.ToDisplayString();
            if ((typeName == "System.Threading.Tasks.Task<TResult>" ||
                 typeName == "System.Threading.Tasks.ValueTask<TResult>") &&
                named.TypeArguments.Length == 1 &&
                named.TypeArguments[0].SpecialType == SpecialType.System_String)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns a human-readable description of the assignment target if it is a persistent
    /// storage location (field, property, or element accessor on a field/property).
    /// Returns <c>null</c> for local variables and other non-persistent targets.
    /// </summary>
    private static string? GetLhsDescription(
        ExpressionSyntax lhs,
        SemanticModel semanticModel,
        System.Threading.CancellationToken cancellationToken)
    {
        // Strip parentheses
        while (lhs is ParenthesizedExpressionSyntax paren)
        {
            lhs = paren.Expression;
        }

        switch (lhs)
        {
            case IdentifierNameSyntax identifier:
            {
                var symbol = semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol;
                if (symbol is IFieldSymbol or IPropertySymbol)
                {
                    return identifier.Identifier.Text;
                }

                return null; // local variable
            }

            case MemberAccessExpressionSyntax memberAccess:
            {
                var symbol = semanticModel.GetSymbolInfo(memberAccess, cancellationToken).Symbol;
                if (symbol is IFieldSymbol or IPropertySymbol)
                {
                    return memberAccess.ToString();
                }

                return null;
            }

            case ElementAccessExpressionSyntax elementAccess:
            {
                // dict[key] = GetNodeId(...) — flag if the receiver is a field/property
                var receiverSymbol = semanticModel
                    .GetSymbolInfo(elementAccess.Expression, cancellationToken).Symbol;
                if (receiverSymbol is IFieldSymbol or IPropertySymbol)
                {
                    return elementAccess.Expression.ToString();
                }

                return null;
            }

            default:
                return null;
        }
    }
}
