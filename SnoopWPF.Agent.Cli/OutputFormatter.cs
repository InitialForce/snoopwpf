namespace SnoopWPF.Agent.Cli;

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using SnoopWPF.Agent.Contracts.Dtos;

/// <summary>
/// Formats DTOs as human-readable text tables or JSON.
/// When <c>--json</c> is in effect callers should call <see cref="WriteJson{T}"/> directly
/// rather than the text-formatting methods.
/// </summary>
internal static class OutputFormatter
{
    // -------------------------------------------------------------------------
    // JSON helpers
    // -------------------------------------------------------------------------

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Serialize any value to indented JSON and write to stdout.</summary>
    internal static void WriteJson<T>(T value)
    {
        Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
    }

    // -------------------------------------------------------------------------
    // Session info
    // -------------------------------------------------------------------------

    internal static void WriteSessionInfo(SessionInfoDto info)
    {
        Console.WriteLine($"Process:     {info.ProcessName} (PID {info.Pid})");
        Console.WriteLine($".NET:        {info.DotnetVersion}");
        Console.WriteLine($"Mutation:    {(info.MutationEnabled ? "enabled" : "disabled")}");
        Console.WriteLine($"Dispatchers: {info.Dispatchers.Count}");

        foreach (var d in info.Dispatchers)
        {
            Console.WriteLine($"  Dispatcher {d.Id} (thread {d.ThreadId}): {d.WindowNodeIds.Count} window(s)");
        }

        if (info.Capabilities.Count > 0)
        {
            Console.WriteLine("Capabilities:");

            foreach (var cap in info.Capabilities)
            {
                Console.WriteLine($"  {cap}");
            }
        }
    }

    // -------------------------------------------------------------------------
    // Windows list
    // -------------------------------------------------------------------------

    internal static void WriteWindows(List<WindowDto> windows)
    {
        if (windows.Count == 0)
        {
            Console.WriteLine("(no windows)");
            return;
        }

        Console.WriteLine($"{"NodeId",-38} {"Title",-40} {"Type",-30} {"Size",14}");
        Console.WriteLine(new string('-', 126));

        foreach (var w in windows)
        {
            var size = $"{w.Width:F0}x{w.Height:F0}";
            Console.WriteLine($"{w.NodeId,-38} {Truncate(w.Title, 40),-40} {Truncate(w.TypeName, 30),-30} {size,14}");
        }
    }

    // -------------------------------------------------------------------------
    // Visual tree
    // -------------------------------------------------------------------------

    internal static void WriteTree(NodeDto root)
    {
        WriteTreeNode(root, prefix: string.Empty, isLast: true);
    }

    private static void WriteTreeNode(NodeDto node, string prefix, bool isLast)
    {
        var connector = isLast ? "L-- " : "|-- ";
        var errorMark = node.HasBindingError ? " [!]" : string.Empty;
        var namePart = string.IsNullOrEmpty(node.Name) ? string.Empty : $" \"{node.Name}\"";
        Console.WriteLine($"{prefix}{connector}{node.TypeName}{namePart}{errorMark}  ({node.NodeId})");

        if (node.Children is null || node.Children.Count == 0)
        {
            return;
        }

        var childPrefix = prefix + (isLast ? "    " : "|   ");

        for (int i = 0; i < node.Children.Count; i++)
        {
            WriteTreeNode(node.Children[i], childPrefix, isLast: i == node.Children.Count - 1);
        }

        if (node.ChildrenTruncated)
        {
            Console.WriteLine($"{childPrefix}L-- ... (truncated)");
        }
    }

    // -------------------------------------------------------------------------
    // Properties table
    // -------------------------------------------------------------------------

    internal static void WriteProperties(IEnumerable<PropertyDto> properties)
    {
        var rows = new List<PropertyDto>(properties);

        if (rows.Count == 0)
        {
            Console.WriteLine("(no properties)");
            return;
        }

        const int nameW = 40;
        const int typeW = 28;
        const int valueW = 48;
        const int srcW = 12;

        Console.WriteLine(
            "Name".PadRight(nameW) + " " +
            "Type".PadRight(typeW) + " " +
            "Value".PadRight(valueW) + " " +
            "Source".PadRight(srcW) + " Flags");
        Console.WriteLine(new string('-', nameW + typeW + valueW + srcW + 20));

        foreach (var p in rows)
        {
            var flags = new StringBuilder();

            if (p.IsLocallySet)
            {
                flags.Append('L');
            }

            if (p.IsDataBound)
            {
                flags.Append('B');
            }

            if (p.HasBindingError)
            {
                flags.Append('!');
            }

            if (p.IsReadOnly)
            {
                flags.Append('R');
            }

            if (p.IsRedacted)
            {
                flags.Append('*');
            }

            Console.WriteLine(
                Truncate(p.Name, nameW).PadRight(nameW) + " " +
                Truncate(p.TypeName, typeW).PadRight(typeW) + " " +
                Truncate(p.IsRedacted ? "[redacted]" : p.Value, valueW).PadRight(valueW) + " " +
                Truncate(p.ValueSource, srcW).PadRight(srcW) + " " +
                flags);

            if (p.HasBindingError && !string.IsNullOrEmpty(p.BindingError))
            {
                Console.WriteLine($"  {">"} Binding error: {p.BindingError}");
            }
        }
    }

    // -------------------------------------------------------------------------
    // Inspect element
    // -------------------------------------------------------------------------

    internal static void WriteInspectElement(InspectElementDto el)
    {
        Console.WriteLine($"NodeId:      {el.NodeId}");
        Console.WriteLine($"Type:        {el.TypeName}");
        Console.WriteLine($"Name:        {el.Name}");
        Console.WriteLine($"DisplayName: {el.DisplayName}");
        Console.WriteLine($"Visible:     {el.IsVisible}");
        Console.WriteLine($"Size:        {el.ActualWidth:F1} x {el.ActualHeight:F1}");
        Console.WriteLine($"Depth:       {el.Depth}");
        Console.WriteLine($"Children:    {el.ChildCount}");
        Console.WriteLine($"Parent:      {el.ParentNodeId}");
        Console.WriteLine($"Dispatcher:  {el.DispatcherId}");
        Console.WriteLine($"DataContext: {el.DataContextType}");
        Console.WriteLine($"BindErrors:  {el.BindingErrorCount}");

        if (el.TriggerCount.HasValue)
        {
            Console.WriteLine($"Triggers:    {el.TriggerCount}");
        }

        if (el.BehaviorCount.HasValue)
        {
            Console.WriteLine($"Behaviors:   {el.BehaviorCount}");
        }

        if (el.Path.Count > 0)
        {
            Console.WriteLine($"Path:        {string.Join(" > ", el.Path)}");
        }
    }

    // -------------------------------------------------------------------------
    // Find results
    // -------------------------------------------------------------------------

    internal static void WriteFindResults(FindElementResultDto result)
    {
        Console.WriteLine($"Found {result.Results.Count} of {result.TotalScanned} scanned{(result.Truncated ? " (truncated)" : string.Empty)}:");
        Console.WriteLine();

        foreach (var hit in result.Results)
        {
            var node = hit.Node;
            var namePart = string.IsNullOrEmpty(node.Name) ? string.Empty : $" \"{node.Name}\"";
            Console.WriteLine($"  {node.TypeName}{namePart}");
            Console.WriteLine($"    NodeId: {node.NodeId}");

            if (hit.Path.Count > 0)
            {
                Console.WriteLine($"    Path:   {string.Join(" > ", hit.Path)}");
            }

            Console.WriteLine();
        }
    }

    // -------------------------------------------------------------------------
    // Diagnostics
    // -------------------------------------------------------------------------

    internal static void WriteDiagnostics(IEnumerable<DiagnosticItemDto> items)
    {
        var rows = new List<DiagnosticItemDto>(items);

        if (rows.Count == 0)
        {
            Console.WriteLine("No diagnostics found.");
            return;
        }

        foreach (var item in rows)
        {
            var levelMarker = item.Level?.ToUpperInvariant() switch
            {
                "ERROR" => "[ERROR]",
                "WARNING" => "[WARN] ",
                "INFO" => "[INFO] ",
                _ => $"[{item.Level ?? "?"}]",
            };

            // Write severity-colored marker to stderr so stdout remains clean.
            var savedFg = Console.ForegroundColor;
            Console.ForegroundColor = item.Level?.ToUpperInvariant() switch
            {
                "ERROR" => ConsoleColor.Red,
                "WARNING" => ConsoleColor.Yellow,
                _ => savedFg,
            };

            Console.Error.Write(levelMarker);
            Console.ForegroundColor = savedFg;
            Console.Error.WriteLine($" {item.Area}: {item.Name}");

            if (!string.IsNullOrWhiteSpace(item.Description))
            {
                Console.Error.WriteLine($"         {item.Description}");
            }

            if (!string.IsNullOrEmpty(item.NodeId))
            {
                Console.Error.WriteLine($"         NodeId: {item.NodeId}");
            }
        }
    }

    // -------------------------------------------------------------------------
    // Screenshot
    // -------------------------------------------------------------------------

    internal static void WriteScreenshotResult(ScreenshotResultDto result, string? outPath)
    {
        var meta = result.Metadata;
        Console.WriteLine($"Captured {meta.Width}x{meta.Height} PNG ({result.PngBytes.Length} bytes).");

        if (!string.IsNullOrEmpty(outPath))
        {
            Console.WriteLine($"Saved to: {outPath}");
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string Truncate(string? value, int maxLength)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..(maxLength - 1)] + "~";
    }
}
