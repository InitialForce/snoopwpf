namespace SnoopWPF.Agent.Remote;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Central <see cref="JsonSerializerOptions"/> factory for all SnoopWPF Agent projects.
/// </summary>
/// <remarks>
/// FX6-C3: Both read and write paths use <c>PropertyNamingPolicy = CamelCase</c> +
/// <c>PropertyNameCaseInsensitive = false</c> so that STJ on net6+ behaves identically
/// to DataContractJsonSerializer on net462 — both reject keys with the wrong case
/// (e.g. <c>"NodeId"</c> instead of <c>"nodeId"</c>).
/// </remarks>
public static class AgentJsonOptions
{
    /// <summary>
    /// Options used by <see cref="FramedJsonTransport"/> for framed pipe messages.
    /// CamelCase names; case-sensitive read (matches DCJS net462 behaviour).
    /// </summary>
    public static readonly JsonSerializerOptions Framed = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        PropertyNameCaseInsensitive = false,
    };

    /// <summary>
    /// Options used by MCP server setup for internal JSON parsing.
    /// CamelCase names; case-sensitive read.
    /// </summary>
    public static readonly JsonSerializerOptions Server = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        PropertyNameCaseInsensitive = false,
    };

    /// <summary>
    /// Options used by MCP tool handlers for serializing output DTOs.
    /// CamelCase names; FailureReason enum as SCREAMING_SNAKE_CASE (PRD §7.2).
    /// Case-sensitive read.
    /// </summary>
    public static readonly JsonSerializerOptions Tools = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        PropertyNameCaseInsensitive = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper) },
    };
}
