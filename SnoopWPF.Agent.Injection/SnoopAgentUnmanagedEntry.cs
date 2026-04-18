// SnoopAgentUnmanagedEntry.cs
//
// [UnmanagedCallersOnly] entry point for the CoreCLR bootstrap DLL
// (SnoopWPF.Agent.Bootstrap — bd-1a9.32 Path A).
//
// The C++ bootstrap resolves this method via:
//   load_assembly_and_get_function_pointer(
//     "SnoopWPF.Agent.Injection.dll",
//     "SnoopWPF.Agent.Injection.SnoopAgentUnmanagedEntry, SnoopWPF.Agent.Injection",
//     "Start",
//     UNMANAGEDCALLERSONLY_METHOD,
//     nullptr, &pfn)
//
// Constraints:
//   - Method must be static.
//   - Parameters must be blittable (char_t* maps to char_t* in hostfxr delegate ABI).
//   - Must NOT throw — exceptions across the native boundary are fatal.
//   - Must be net6+ (UnmanagedCallersOnly requires CoreCLR).

#if NET6_0_OR_GREATER

namespace SnoopWPF.Agent.Injection;

using System;
using System.Runtime.InteropServices;

/// <summary>
/// Unmanaged entry point for the CoreCLR bootstrap DLL (bd-1a9.32 Path A).
/// Called from <c>SnoopWPF.Agent.Bootstrap.dll</c> via
/// <c>load_assembly_and_get_function_pointer</c>.
/// </summary>
public static class SnoopAgentUnmanagedEntry
{
    /// <summary>
    /// Bootstrap entry — called from the native bootstrap DLL worker thread
    /// via hostfxr <c>load_assembly_and_get_function_pointer</c>.
    /// </summary>
    /// <param name="settingsFilePath">
    /// Native pointer (nint) to a null-terminated UTF-16 wide string containing
    /// the path to the XML settings file.  May be IntPtr.Zero or point to an
    /// empty string in smoke-test mode.
    /// </param>
    /// <param name="settingsFilePathLength">
    /// Number of wchar_t characters in <paramref name="settingsFilePath"/>
    /// (not including the null terminator).
    /// </param>
    /// <returns>0 on success; 1 on failure.</returns>
    [UnmanagedCallersOnly]
    public static int Start(nint settingsFilePath, int settingsFilePathLength)
    {
        try
        {
            // Convert native wchar_t* → managed string.
            // nint avoids unsafe context; PtrToStringUni handles UTF-16.
            string path = (settingsFilePath != 0 && settingsFilePathLength > 0)
                ? Marshal.PtrToStringUni(settingsFilePath, settingsFilePathLength)!
                : string.Empty;

            System.Diagnostics.Debug.WriteLine(
                $"[SnoopAgentUnmanagedEntry] Start called, settingsPath='{path}'");

            // Smoke-test mode: no settings file → confirm managed entry ran and return.
            if (string.IsNullOrEmpty(path))
            {
                System.Diagnostics.Debug.WriteLine(
                    "[SnoopAgentUnmanagedEntry] Smoke-test mode — no settings file, returning 0.");
                return 0;
            }

            // Forward to the existing injection entry point.
            return SnoopAgentEntryPoint.Start(path);
        }
        catch (Exception ex)
        {
            // Never let an exception escape across the native boundary.
            System.Diagnostics.Debug.WriteLine(
                $"[SnoopAgentUnmanagedEntry] Unhandled exception: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }
}

#endif
