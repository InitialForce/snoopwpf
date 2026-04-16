namespace SnoopWPF.Agent.Analyzers;

using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
/// SWPF0011 — Warns when a string literal matching the <c>path=</c> locator pattern is
/// stored in a field, constant, array literal, or dictionary value.
///
/// <c>path=</c> locators encode a visual-tree position that is derived from sibling index.
/// Three sibling Buttons in a StackPanel all match the same <c>path=</c> expression, so
/// the locator is not durable under sibling reordering or count changes.  Storing such a
/// locator will silently resolve to the wrong element after UI changes.  Prefer
/// <c>automationId=</c> or <c>viewModel=</c> forms for any string that must be persisted.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PathLocator0011Analyzer : DiagnosticAnalyzer
{
    /// <summary>The diagnostic ID for SWPF0011.</summary>
    public const string DiagnosticId = "SWPF0011";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "Stored path= locator — fragile under sibling reordering",
        messageFormat: "String literal '{0}' contains a path= locator; storing it in '{1}' is fragile. Prefer automationId= or viewModel= for persistence.",
        category: "SnoopWPF.Agent",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "path= locators encode a visual-tree position that depends on sibling order. "
                   + "When siblings are added, removed, or reordered the locator silently resolves "
                   + "to a different element. Store automationId= or viewModel= locators instead.");

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Field declarations: private string _loc = "path=Window\\Grid";
        // Also catches const declarations.
        context.RegisterSyntaxNodeAction(AnalyzeFieldDeclaration, SyntaxKind.FieldDeclaration);

        // Assignment expressions: _field = "path=...";  _dict["k"] = "path=...";
        context.RegisterSyntaxNodeAction(AnalyzeAssignment, SyntaxKind.SimpleAssignmentExpression);

        // Array / collection initializer elements that end up in a field/property init.
        // We walk these via FieldDeclaration already (the initializer subtree is included),
        // but element access on field-backed collections in assignments is also covered above.
    }

    // ─── Syntax-node handlers ────────────────────────────────────────────────────

    private static void AnalyzeFieldDeclaration(SyntaxNodeAnalysisContext ctx)
    {
        var fieldDecl = (FieldDeclarationSyntax)ctx.Node;

        foreach (var variable in fieldDecl.Declaration.Variables)
        {
            if (variable.Initializer is null)
            {
                continue;
            }

            // Walk every string literal inside the initializer (handles array / dict literals)
            foreach (var literal in variable.Initializer.Value
                         .DescendantNodesAndSelf()
                         .OfType<LiteralExpressionSyntax>())
            {
                if (!IsPathLocatorLiteral(literal, out var rawValue))
                {
                    continue;
                }

                ctx.ReportDiagnostic(Diagnostic.Create(Rule, literal.GetLocation(),
                    rawValue, variable.Identifier.Text));
            }
        }
    }

    private static void AnalyzeAssignment(SyntaxNodeAnalysisContext ctx)
    {
        var assignment = (AssignmentExpressionSyntax)ctx.Node;

        // Walk every string literal on the right-hand side (covers dict-value assignments)
        foreach (var literal in assignment.Right
                     .DescendantNodesAndSelf()
                     .OfType<LiteralExpressionSyntax>())
        {
            if (!IsPathLocatorLiteral(literal, out var rawValue))
            {
                continue;
            }

            var lhs = assignment.Left;
            var targetDescription = GetLhsDescription(lhs, ctx.SemanticModel,
                ctx.CancellationToken);
            if (targetDescription is null)
            {
                return; // local variable — not a persistent storage location
            }

            ctx.ReportDiagnostic(Diagnostic.Create(Rule, literal.GetLocation(),
                rawValue, targetDescription));
        }
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> when <paramref name="literal"/> is a string literal whose value
    /// contains the substring <c>path=</c> (case-sensitive, per WpfLocator grammar).
    /// </summary>
    private static bool IsPathLocatorLiteral(
        LiteralExpressionSyntax literal,
        out string rawValue)
    {
        rawValue = string.Empty;

        if (!literal.IsKind(SyntaxKind.StringLiteralExpression) &&
            !literal.IsKind(SyntaxKind.Utf8StringLiteralExpression))
        {
            return false;
        }

        var token = literal.Token;
        // token.ValueText gives the unescaped string value
        var text = token.ValueText;
        if (text is null)
        {
            return false;
        }

        if (!text.Contains("path=", StringComparison.Ordinal))
        {
            return false;
        }

        rawValue = text;
        return true;
    }

    /// <summary>
    /// Returns a human-readable description of the assignment target when it is a persistent
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
                // _dict["key"] = "path=..." — flag if the receiver is a field/property
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
