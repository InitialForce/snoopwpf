namespace SnoopWPF.Agent.Engine.Infrastructure;

using System;
using System.Security;
using System.Windows.Controls;

/// <summary>
/// Determines whether a property should be redacted for security reasons.
/// Pure function — no WPF dependency at runtime except for the PasswordBox type check.
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

        // PasswordBox.Password — special-cased: PasswordBox component with "Password" name
        if (string.Equals(propertyName, "Password", StringComparison.OrdinalIgnoreCase)
            && propertyType is not null
            && (typeof(PasswordBox).IsAssignableFrom(propertyType)
                || (propertyType.DeclaringType is not null && typeof(PasswordBox).IsAssignableFrom(propertyType.DeclaringType))))
        {
            return true;
        }

        // Contains-match on keyword list (case-insensitive)
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
