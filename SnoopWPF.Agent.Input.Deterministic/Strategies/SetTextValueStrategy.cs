namespace SnoopWPF.Agent.Input.Deterministic.Strategies;

using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;

/// <summary>
/// L0 strategy: sets text content directly on <see cref="TextBox"/>, <see cref="PasswordBox"/>,
/// and <see cref="RichTextBox"/> via dependency-property <c>SetValue</c> — no raw Win32 input.
/// </summary>
/// <remarks>
/// Control routing:
/// <list type="bullet">
///   <item><see cref="TextBox"/> — <c>SetValue(TextBox.TextProperty, value)</c>.</item>
///   <item><see cref="PasswordBox"/> — sets the <c>Password</c> CLR property directly.
///         The input value is treated as SensitiveText (S3); it is redacted in any logging
///         path unless <c>AllowSensitiveRetention=true</c> is set in the session policy.</item>
///   <item><see cref="RichTextBox"/> — the flow document is replaced with a single
///         <see cref="Paragraph"/> containing the plain-text value.</item>
/// </list>
///
/// Gate: <see cref="InputIntentKind.SetTextValue"/> requires mutation to be enabled
/// (checked by <see cref="InputStrategySelector"/> before this strategy is selected).
/// </remarks>
public sealed class SetTextValueStrategy : IDeterministicInputStrategy
{
    /// <inheritdoc/>
    public InputTier Tier => InputTier.L0;

    /// <inheritdoc/>
    /// <remarks>
    /// Returns <see langword="true"/> when the target is a <see cref="TextBox"/>,
    /// <see cref="PasswordBox"/>, or <see cref="RichTextBox"/> and the intent kind is
    /// <see cref="InputIntentKind.SetTextValue"/>.
    /// </remarks>
    public bool CanHandle(InputIntent intent, DependencyObject target)
    {
        if (intent is null || intent.Kind != InputIntentKind.SetTextValue)
        {
            return false;
        }

        return target is TextBox or PasswordBox or RichTextBox;
    }

    /// <inheritdoc/>
    public DeterministicInputResult Invoke(DependencyObject target, InputIntent intent, CancellationToken ct)
    {
        // Extract the text value from intent arguments.
        var value = GetArgument(intent, "value") ?? string.Empty;

        if (target is TextBox textBox)
        {
            return SetTextBoxValue(textBox, value);
        }

        if (target is PasswordBox passwordBox)
        {
            return SetPasswordBoxValue(passwordBox, value);
        }

        if (target is RichTextBox richTextBox)
        {
            return SetRichTextBoxValue(richTextBox, value);
        }

        // Unreachable — CanHandle already filtered.
        return new DeterministicInputResult
        {
            Success = false,
            FailureReason = FailureReason.PatternNotSupported,
            ChosenTier = InputTier.L0,
        };
    }

    // -------------------------------------------------------------------------
    // Per-control implementations
    // -------------------------------------------------------------------------

    private static DeterministicInputResult SetTextBoxValue(TextBox textBox, string value)
    {
        var previousValue = textBox.Text;
        textBox.SetValue(TextBox.TextProperty, value);

        System.Diagnostics.Trace.WriteLine(
            $"[SnoopWPF.Agent] SetTextValue(TextBox): previousValue={previousValue}");

        return new DeterministicInputResult
        {
            Success = true,
            PreviousValue = previousValue,
            ChosenTier = InputTier.L0,
        };
    }

    private static DeterministicInputResult SetPasswordBoxValue(PasswordBox passwordBox, string value)
    {
        // PasswordBox.Password is not a DP; use the CLR property which internally calls
        // SetValue(PasswordBox.PasswordProperty, ...) on the underlying secure string.
        // We capture a placeholder previous value (never the actual secret) so the
        // SensitiveText (S3) rule is respected: PasswordBox input is redacted in logs.
        const string redactedMarker = "[REDACTED]";

        System.Diagnostics.Trace.WriteLine(
            "[SnoopWPF.Agent] SetTextValue(PasswordBox): value=<redacted>");

        // PasswordBox.Password is a CLR property backed by SecureString, not a standard DP;
        // there is no public PasswordProperty DependencyProperty. Use the CLR setter directly.
        passwordBox.Password = value;

        return new DeterministicInputResult
        {
            Success = true,
            PreviousValue = redactedMarker,
            ChosenTier = InputTier.L0,
        };
    }

    private static DeterministicInputResult SetRichTextBoxValue(RichTextBox richTextBox, string value)
    {
        // Capture previous plain-text value from the flow document.
        var previousValue = GetRichTextPlainText(richTextBox);

        // Replace the document with a single paragraph containing the new plain text.
        richTextBox.Document = new FlowDocument(new Paragraph(new Run(value)));

        System.Diagnostics.Trace.WriteLine(
            $"[SnoopWPF.Agent] SetTextValue(RichTextBox): previousValue={previousValue}");

        return new DeterministicInputResult
        {
            Success = true,
            PreviousValue = previousValue,
            ChosenTier = InputTier.L0,
        };
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string? GetArgument(InputIntent intent, string name)
    {
        if (intent.Arguments is null)
        {
            return null;
        }

        foreach (var pair in intent.Arguments)
        {
            if (string.Equals(pair.Name, name, System.StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static string GetRichTextPlainText(RichTextBox richTextBox)
    {
        var start = richTextBox.Document.ContentStart;
        var end = richTextBox.Document.ContentEnd;
        return new TextRange(start, end).Text ?? string.Empty;
    }
}
