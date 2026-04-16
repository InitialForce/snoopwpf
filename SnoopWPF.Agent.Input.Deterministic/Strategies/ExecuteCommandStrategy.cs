namespace SnoopWPF.Agent.Input.Deterministic.Strategies;

using System.Threading;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using SnoopWPF.Agent.Contracts;
using SnoopWPF.Agent.Contracts.Dtos;

/// <summary>
/// L0 strategy: resolves <see cref="ButtonBase.CommandProperty"/> on the target element,
/// checks <see cref="ICommand.CanExecute"/>, and invokes <see cref="ICommand.Execute"/>.
/// </summary>
/// <remarks>
/// This is the preferred tier for command-bound controls — it avoids raw Win32 input and
/// operates entirely through the WPF command system (PRD §4.4, M2-01).
///
/// Gate: <see cref="InputIntentKind.ExecuteCommand"/> requires mutation to be enabled
/// (checked by <see cref="InputStrategySelector"/> before this strategy is selected).
/// </remarks>
public sealed class ExecuteCommandStrategy : IDeterministicInputStrategy
{
    /// <inheritdoc/>
    public InputTier Tier => InputTier.L0;

    /// <inheritdoc/>
    /// <remarks>
    /// Returns <see langword="true"/> when the target is a <see cref="DependencyObject"/>
    /// with a non-null value for <see cref="ButtonBase.CommandProperty"/>.
    /// </remarks>
    public bool CanHandle(InputIntent intent, DependencyObject target)
    {
        if (intent is null || intent.Kind != InputIntentKind.ExecuteCommand)
        {
            return false;
        }

        // Resolve ICommand from the CommandProperty DP.
        var command = target.GetValue(ButtonBase.CommandProperty) as ICommand;
        return command is not null;
    }

    /// <inheritdoc/>
    public DeterministicInputResult Invoke(DependencyObject target, InputIntent intent, CancellationToken ct)
    {
        // Resolve command parameter (optional — stored on CommandParameterProperty).
        var commandParameter = target.GetValue(ButtonBase.CommandParameterProperty);

        // Resolve command — CanHandle already verified it is non-null.
        var command = (ICommand)target.GetValue(ButtonBase.CommandProperty)!;

        // CanExecute gate.
        if (!command.CanExecute(commandParameter))
        {
            return new DeterministicInputResult
            {
                Success = false,
                FailureReason = FailureReason.CannotExecuteCommand,
                ChosenTier = InputTier.L0,
            };
        }

        // Capture previousValue: stringify the CommandParameter (best available L0 state proxy).
        var previousValue = commandParameter?.ToString() ?? string.Empty;

        // Execute the command.
        command.Execute(commandParameter);

        return new DeterministicInputResult
        {
            Success = true,
            PreviousValue = previousValue,
            ChosenTier = InputTier.L0,
        };
    }
}
