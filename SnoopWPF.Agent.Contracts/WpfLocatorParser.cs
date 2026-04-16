namespace SnoopWPF.Agent.Contracts;

using System;
using System.Collections.Generic;

/// <summary>
/// Parses the four $locator grammar forms defined in PRD §6.
/// </summary>
/// <remarks>
/// Grammar summary:
/// <code>
///   automationId=&lt;value&gt;
///   viewModel=&lt;typeShort&gt;[, property=&lt;p&gt;, value=&lt;v&gt;]
///   type=&lt;typeShort&gt;[, name=&lt;n&gt;]
///   path=&lt;token&gt;(\&lt;token&gt;)*
/// </code>
/// Unknown keys throw <see cref="LocatorParseException"/>.
/// Raw strings longer than 2048 characters throw <see cref="LocatorParseException"/>.
/// </remarks>
public static class WpfLocatorParser
{
    private const int MaxRawLength = 2048;

    /// <summary>
    /// Parses <paramref name="raw"/> into a <see cref="WpfLocator"/>.
    /// </summary>
    /// <param name="raw">The locator string supplied by the caller.</param>
    /// <returns>A fully populated <see cref="WpfLocator"/>.</returns>
    /// <exception cref="LocatorParseException">
    /// Thrown when <paramref name="raw"/> exceeds <see cref="MaxRawLength"/> chars,
    /// contains an unrecognised key, or violates a form's grammar.
    /// </exception>
    public static WpfLocator Parse(string raw)
    {
        if (raw is null)
        {
            throw new LocatorParseException("Locator string must not be null.");
        }

        if (raw.Length > MaxRawLength)
        {
            throw new LocatorParseException(
                $"Locator string length {raw.Length} exceeds maximum {MaxRawLength} characters.");
        }

        var parts = SplitParts(raw);

        if (parts.TryGetValue("automationId", out var automationIdValue))
        {
            RequireNoUnknownKeys(parts, raw, "automationId");
            return new WpfLocator
            {
                Form = WpfLocatorForm.AutomationId,
                Value = automationIdValue,
                Raw = raw,
            };
        }

        if (parts.TryGetValue("viewModel", out var viewModelValue))
        {
            RequireNoUnknownKeys(parts, raw, "viewModel", "property", "value");
            parts.TryGetValue("property", out var property);
            parts.TryGetValue("value", out var vmValue);
            return new WpfLocator
            {
                Form = WpfLocatorForm.ViewModel,
                Value = viewModelValue,
                Raw = raw,
                Qualifier = property,
                QualifierValue = vmValue,
            };
        }

        if (parts.TryGetValue("type", out var typeValue))
        {
            RequireNoUnknownKeys(parts, raw, "type", "name");
            parts.TryGetValue("name", out var name);
            return new WpfLocator
            {
                Form = WpfLocatorForm.TypeName,
                Value = typeValue,
                Raw = raw,
                Qualifier = name,
            };
        }

        if (parts.TryGetValue("path", out var pathValue))
        {
            RequireNoUnknownKeys(parts, raw, "path");
            return new WpfLocator
            {
                Form = WpfLocatorForm.Path,
                Value = pathValue,
                Raw = raw,
            };
        }

        throw new LocatorParseException(
            $"No recognised locator key found in: \"{Truncate(raw)}\".");
    }

    /// <summary>
    /// Splits "key=value[, key=value]*" into a dictionary.
    /// Pairs are separated by ", " (comma-space). Values may contain backslashes.
    /// </summary>
    private static Dictionary<string, string> SplitParts(string raw)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        var segments = raw.Split(new[] { ", " }, StringSplitOptions.None);

        foreach (var segment in segments)
        {
            var eqIndex = segment.IndexOf("=", StringComparison.Ordinal);
            if (eqIndex <= 0)
            {
                result[segment.Trim()] = string.Empty;
                continue;
            }

            var key = segment.Substring(0, eqIndex).Trim();
            var val = segment.Substring(eqIndex + 1);
            result[key] = val;
        }

        return result;
    }

    private static void RequireNoUnknownKeys(
        Dictionary<string, string> parts,
        string raw,
        params string[] allowed)
    {
        var allowedSet = new HashSet<string>(allowed, StringComparer.Ordinal);
        foreach (var key in parts.Keys)
        {
            if (!allowedSet.Contains(key))
            {
                throw new LocatorParseException(
                    $"Unknown locator key \"{key}\" in: \"{Truncate(raw)}\".");
            }
        }
    }

    private static string Truncate(string s, int max = 120)
    {
        return s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}
