namespace SnoopWPF.Agent.Contracts;

using System;

/// <summary>
/// Thrown by <see cref="WpfLocatorParser.Parse"/> when a locator string is invalid.
/// </summary>
public sealed class LocatorParseException : Exception
{
    /// <summary>Initializes a new instance with a message.</summary>
    public LocatorParseException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance with a message and inner exception.</summary>
    public LocatorParseException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
