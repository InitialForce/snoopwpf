namespace SnoopWPF.Agent.Engine.Infrastructure;

using System;
using System.Data.Common;
using System.Net;
using System.Reflection;
using System.Security;
using SnoopWPF.Agent.Contracts;

/// <summary>
/// Determines whether a property should be redacted for security reasons.
/// Pure function — no WPF dependency at runtime.
/// PasswordBox.Password is covered by the "password" keyword in <see cref="SensitiveKeywords"/>.
/// </summary>
public static class RedactionFilter
{
    /// <summary>
    /// MF-10 structural-sensitivity roots. Any value whose runtime type is assignable to
    /// one of these types is redacted before <c>ToString()</c> is ever called.
    /// PRD §9.2, global rule S3.
    /// </summary>
    private static readonly Type[] StructuralSensitiveRoots =
    {
        typeof(SecureString),
        typeof(NetworkCredential),
        typeof(DbConnectionStringBuilder),
    };

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
    /// MF-10 — inspects the runtime type of <paramref name="value"/> for structural sensitivity
    /// BEFORE any <c>ToString()</c> call occurs.
    /// Returns <see langword="true"/> when the value's runtime type is assignable to any of
    /// <see cref="StructuralSensitiveRoots"/> or is decorated with <see cref="SensitiveAttribute"/>.
    /// </summary>
    /// <param name="value">The property value to inspect (may be null).</param>
    public static bool IsStructurallySensitive(object? value)
    {
        if (value is null)
        {
            return false;
        }

        var type = value.GetType();

        // Check assignability to each structural root.
        foreach (var root in StructuralSensitiveRoots)
        {
            if (root.IsAssignableFrom(type))
            {
                return true;
            }
        }

        // Check for [Sensitive] attribute on the runtime type via CustomAttributeData
        // (avoids loading the attribute type into the checked assembly's context).
        foreach (var cad in CustomAttributeData.GetCustomAttributes(type))
        {
            if (cad.AttributeType == typeof(SensitiveAttribute))
            {
                return true;
            }
        }

        return false;
    }

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
    /// MF-10: structural-sensitivity check fires BEFORE <c>ToString()</c> is invoked.
    /// </summary>
    public static string Redact(string propertyName, Type? propertyType, object? value)
    {
        // MF-10: inspect runtime type BEFORE invoking ToString().
        if (IsStructurallySensitive(value))
        {
            return "[REDACTED]";
        }

        if (IsRedacted(propertyName, propertyType))
        {
            return "[REDACTED]";
        }

        return value?.ToString() ?? string.Empty;
    }
}
