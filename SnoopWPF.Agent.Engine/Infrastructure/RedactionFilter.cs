namespace SnoopWPF.Agent.Engine.Infrastructure;

using System;
using System.Security;

/// <summary>
/// Determines whether a property should be redacted for security reasons.
/// Pure function — no WPF dependency at runtime.
/// PasswordBox.Password is covered by the "password" keyword in <see cref="SensitiveKeywords"/>.
/// </summary>
public static class RedactionFilter
{
    /// <summary>
    /// Sensitive property name substrings. All matches are case-insensitive contains.
    /// </summary>
    private static readonly string[] SensitiveKeywords =
    {
        "password",
        "passwd",
        "pwd",
        "secret",
        "apikey",
        "connectionstring",
        "connstr",
        "credential",
        "privatekey",
        "sharedkey",
        "cookie",
        "sessionkey",
        "authorization",
        "authtoken",
        "authkey",
        "accesstoken",
        "bearertoken",
        "refreshtoken",
        "sessiontoken",
        "sastoken",
        "jwttoken",
    };

    /// <summary>
    /// Returns true if the property should be redacted.
    /// </summary>
    /// <param name="propertyName">The property name to check.</param>
    /// <param name="propertyType">The property's declared type (may be null if unknown).</param>
    public static bool IsRedacted(string propertyName, Type? propertyType)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            return false;
        }

        // SecureString-typed properties are always redacted
        if (propertyType is not null && typeof(SecureString).IsAssignableFrom(propertyType))
        {
            return true;
        }

        // Contains-match on keyword list (case-insensitive).
        // Note: PasswordBox.Password is covered by the "password" keyword below —
        // propertyType here is the VALUE type (e.g. string), not the declaring type,
        // so a PasswordBox-specific type check would never fire.
        foreach (var keyword in SensitiveKeywords)
        {
            if (propertyName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns "[REDACTED]" if the property is sensitive, otherwise returns the value's string representation.
    /// </summary>
    public static string Redact(string propertyName, Type? propertyType, object? value)
    {
        if (IsRedacted(propertyName, propertyType))
        {
            return "[REDACTED]";
        }

        return value?.ToString() ?? string.Empty;
    }
}
