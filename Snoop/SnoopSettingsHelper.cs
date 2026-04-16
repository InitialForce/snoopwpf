namespace Snoop;

using System;
using Snoop.Data;

/// <summary>
/// Helper for creating <see cref="TransientSettingsData"/> from the current Snoop GUI settings.
/// </summary>
internal static class SnoopSettingsHelper
{
    public static TransientSettingsData CreateTransientSettingsData(SnoopStartTarget startTarget, IntPtr targetWindowHandle)
    {
        var settings = Settings.Default;

        return new TransientSettingsData
        {
            StartTarget = startTarget,
            TargetWindowHandle = targetWindowHandle.ToInt64(),

            MultipleAppDomainMode = settings.MultipleAppDomainMode,
            MultipleDispatcherMode = settings.MultipleDispatcherMode,
            SetOwnerWindow = settings.SetOwnerWindow,
            ShowActivated = settings.ShowActivated,
            EnableDiagnostics = settings.EnableDiagnostics,
            ILSpyPath = settings.ILSpyPath
        };
    }
}
