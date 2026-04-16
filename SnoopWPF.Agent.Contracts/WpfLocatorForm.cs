namespace SnoopWPF.Agent.Contracts;

/// <summary>
/// Discriminates the four locator forms defined in PRD §6.
/// Values are stable — do not reorder (wire format).
/// </summary>
public enum WpfLocatorForm
{
    /// <summary>automationId=&lt;value&gt; — most stable; preferred.</summary>
    AutomationId = 0,

    /// <summary>viewModel=&lt;typeShort&gt;[, property=&lt;p&gt;, value=&lt;v&gt;] — redaction-sensitive.</summary>
    ViewModel = 1,

    /// <summary>type=&lt;typeShort&gt;[, name=&lt;n&gt;] — structural.</summary>
    TypeName = 2,

    /// <summary>path=&lt;tokens separated by backslash&gt; — weakest; fragile under tree changes.</summary>
    Path = 3,
}
