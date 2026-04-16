// ReSharper disable once CheckNamespace
namespace Snoop.Data;

using System;
using System.IO;
using System.Xml.Serialization;
using JetBrains.Annotations;
using Snoop.Core;
using Snoop.Infrastructure;

[PublicAPI]
public sealed class TransientSettingsData
{
    private static readonly XmlSerializer serializer = new(typeof(TransientSettingsData));

    public static TransientSettingsData? Current { get; private set; }

    public SnoopStartTarget StartTarget { get; set; } = SnoopStartTarget.SnoopUI;

    public MultipleAppDomainMode MultipleAppDomainMode { get; set; } = MultipleAppDomainMode.Ask;

    public MultipleDispatcherMode MultipleDispatcherMode { get; set; } = MultipleDispatcherMode.Ask;

    public bool SetOwnerWindow { get; set; } = true;

    public bool ShowActivated { get; set; } = true;

    public long TargetWindowHandle { get; set; }

    public string? ILSpyPath { get; set; } = "%path%";

    public bool EnableDiagnostics { get; set; } = true;

    public string? SnoopInstallPath { get; set; } = Environment.GetEnvironmentVariable(SettingsHelper.SNOOP_INSTALL_PATH_ENV_VAR);

    /// <summary>Named-pipe name used in headless/injection mode. Nullable — XmlSerializer handles this cleanly.</summary>
    public string? PipeName { get; set; }

    /// <summary>Session bearer token used in headless/injection mode. Nullable — never log this value.</summary>
    public string? SessionToken { get; set; }

    public string WriteToFile()
    {
        var settingsFile = Path.GetTempFileName();

        // Do NOT log settingsFile path — it may reside in a path that contains user info,
        // and the file itself contains PipeName/SessionToken.
        LogHelper.WriteLine("Writing transient settings file.");

        using var stream = new FileStream(settingsFile, FileMode.Create);
        serializer.Serialize(stream, this);

        return settingsFile;
    }

    public static TransientSettingsData LoadCurrentIfRequired(string settingsFile)
    {
        if (Current is not null)
        {
            return Current;
        }

        return LoadCurrent(settingsFile);
    }

    public static TransientSettingsData LoadCurrent(string settingsFile)
    {
        // Do NOT log settingsFile path — it may contain sensitive location information.
        LogHelper.WriteLine("Loading transient settings file.");

        using var stream = new FileStream(settingsFile, FileMode.Open);
        Current = (TransientSettingsData?)serializer.Deserialize(stream) ?? new TransientSettingsData();

        Environment.SetEnvironmentVariable(SettingsHelper.SNOOP_INSTALL_PATH_ENV_VAR, Current.SnoopInstallPath, EnvironmentVariableTarget.Process);

        return Current;
    }
}

[PublicAPI]
public enum MultipleAppDomainMode
{
    Ask = 0,
    AlwaysUse = 1,
    NeverUse = 2
}

[PublicAPI]
public enum MultipleDispatcherMode
{
    Ask = 0,
    AlwaysUse = 1,
    NeverUse = 2
}

[PublicAPI]
public enum SnoopStartTarget
{
    SnoopUI = 0,
    Zoomer = 1,

    /// <summary>
    /// Headless agent mode. No Snoop UI is shown. The agent is created via
    /// <see cref="SnoopManager.HeadlessAgentFactory"/> (NuGet/in-process) or directly by
    /// <c>SnoopAgentEntryPoint.Start()</c> (injection mode, bypasses SnoopManager).
    /// </summary>
    HeadlessAgent = 2
}
