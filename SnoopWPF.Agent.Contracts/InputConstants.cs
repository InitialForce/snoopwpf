namespace SnoopWPF.Agent.Contracts;

/// <summary>
/// Engine-layer input caps. Match the bounds advertised in MCP tool JSON schemas.
/// </summary>
public static class InputConstants
{
    /// <summary>Maximum pageSize accepted by cursor-paginated inspector methods.</summary>
    public const int MaxPageSize = 200;

    /// <summary>Maximum tree traversal depth for GetVisualTree / GetLogicalTree / GetAutomationTree.</summary>
    public const int MaxTreeDepth = 10;
}
