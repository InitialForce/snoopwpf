namespace Snoop;

using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Snoop.Data;
using Snoop.Infrastructure;

public class ProcessInfo
{
    private const string SnoopCoreAssemblyName = "Snoop.Core";
    private const string SnoopManagerTypeName = "Snoop.Infrastructure.SnoopManager";
    private const string StartSnoopMethodName = "StartSnoop";

    private bool? isOwningProcessElevated;

    public ProcessInfo(int processId)
        : this(Process.GetProcessById(processId))
    {
    }

    public ProcessInfo(Process process)
    {
        this.Process = process;
    }

    public Process Process { get; }

    public bool IsProcessElevated => this.isOwningProcessElevated ??= NativeMethods.IsProcessElevated(this.Process);

    public AttachResult Snoop(IntPtr targetHwnd, TransientSettingsData transientSettingsData)
    {
        if (Application.Current?.CheckAccess() == true)
        {
            Mouse.OverrideCursor = Cursors.Wait;
        }

        try
        {
            InjectorLauncherManager.Launch(this, targetHwnd, SnoopCoreAssemblyName, SnoopManagerTypeName, StartSnoopMethodName, transientSettingsData.WriteToFile());
        }
        catch (Exception e)
        {
            return new AttachResult(e);
        }
        finally
        {
            if (Application.Current?.CheckAccess() == true)
            {
                Mouse.OverrideCursor = null;
            }
        }

        return new AttachResult();
    }

    public AttachResult Magnify(IntPtr targetHwnd, TransientSettingsData transientSettingsData)
    {
        if (Application.Current?.CheckAccess() == true)
        {
            Mouse.OverrideCursor = Cursors.Wait;
        }

        try
        {
            InjectorLauncherManager.Launch(this, targetHwnd, SnoopCoreAssemblyName, SnoopManagerTypeName, StartSnoopMethodName, transientSettingsData.WriteToFile());
        }
        catch (Exception e)
        {
            return new AttachResult(e);
        }
        finally
        {
            if (Application.Current?.CheckAccess() == true)
            {
                Mouse.OverrideCursor = null;
            }
        }

        return new AttachResult();
    }
}
