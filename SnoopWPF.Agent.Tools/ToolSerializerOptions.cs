namespace SnoopWPF.Agent.Tools;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Shared JsonSerializerOptions used by all MCP tool handlers.
/// Ensures FailureReason enum values serialise as SCREAMING_SNAKE_CASE per PRD §7.2.
/// </summary>
internal static class ToolSerializerOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper) },
    };
}
