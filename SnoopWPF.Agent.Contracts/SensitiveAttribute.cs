namespace SnoopWPF.Agent.Contracts;

using System;

/// <summary>
/// Opt-in marker attribute that signals the structural-redaction map (MF-10) to redact
/// the value of the decorated property, field, or type before surfacing it to the MCP layer.
/// Recognised at every <c>Redact()</c> call via
/// <c>GetCustomAttribute&lt;SensitiveAttribute&gt;()</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Class
              | AttributeTargets.Struct | AttributeTargets.Interface, AllowMultiple = false)]
public sealed class SensitiveAttribute : Attribute
{
}
