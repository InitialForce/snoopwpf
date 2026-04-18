// SnoopWPF.Agent.Bootstrap Smoke Test
//
// Validates the hostfxr bootstrap chain (bd-1a9.32 Path A) in-process.
// This is NOT injection — it runs bootstrap DLL in the same process via LoadLibrary,
// proving the hostfxr chain works before the injector (bd-1a9.33) is built.
//
// What this proves:
//   1. SnoopWPF.Agent.Bootstrap.dll loads cleanly (DllMain returns TRUE).
//   2. hostfxr.dll is present in a net10.0-windows process.
//   3. hostfxr_initialize_for_runtime_config + hostfxr_get_runtime_delegate resolve.
//   4. load_assembly_and_get_function_pointer finds SnoopAgentUnmanagedEntry.Start.
//   5. The managed entry runs and returns 0 (smoke-test mode with empty path).
//
// Run via:
//   cmd.exe /c "dotnet run --project SnoopWPF.Agent.Bootstrap.SmokeTest\SnoopWPF.Agent.Bootstrap.SmokeTest.csproj"

using System.Runtime.InteropServices;
using System.Diagnostics;

Console.WriteLine("[SmokeTest] SnoopWPF.Agent.Bootstrap hostfxr chain smoke test");
Console.WriteLine($"[SmokeTest] Process TFM: net10.0-windows (CoreCLR)");
Console.WriteLine($"[SmokeTest] PID: {Environment.ProcessId}");

// ---------------------------------------------------------------------------
// Step 1: Verify hostfxr.dll is loaded in this process (it always is on CoreCLR).
// ---------------------------------------------------------------------------
var hHostFxr = NativeMethods.GetModuleHandleW("hostfxr.dll");
if (hHostFxr == IntPtr.Zero)
{
    Console.Error.WriteLine("[SmokeTest] FAIL: hostfxr.dll not found in process — unexpected for CoreCLR.");
    return 1;
}
Console.WriteLine($"[SmokeTest] PASS: hostfxr.dll loaded at 0x{hHostFxr:X}");

// ---------------------------------------------------------------------------
// Step 2: Load the bootstrap DLL.
//
// The bootstrap DLL must be next to this exe's output directory.
// The smoke test project copies it via a post-build / manual step, OR
// we point to the bin/Debug/ output from the vcxproj.
//
// For the smoke test we try these locations in order:
//   a) Same directory as the smoke test exe
//   b) ..\..\bin\Debug\SnoopWPF.Agent.Bootstrap.dll  (snoopwpf\bin\Debug)
// ---------------------------------------------------------------------------
string exeDir = AppContext.BaseDirectory;
string bootstrapDll = Path.Combine(exeDir, "SnoopWPF.Agent.Bootstrap.dll");

if (!File.Exists(bootstrapDll))
{
    // Try the build output directory (relative to the project structure)
    string repoRoot = Path.GetFullPath(Path.Combine(exeDir, "../../../../.."));
    bootstrapDll = Path.Combine(repoRoot, "bin", "Debug", "SnoopWPF.Agent.Bootstrap.dll");
}

if (!File.Exists(bootstrapDll))
{
    Console.Error.WriteLine($"[SmokeTest] SKIP: SnoopWPF.Agent.Bootstrap.dll not found.");
    Console.Error.WriteLine($"[SmokeTest] Build the C++ project first:");
    Console.Error.WriteLine($"[SmokeTest]   msbuild SnoopWPF.Agent.Bootstrap\\SnoopWPF.Agent.Bootstrap.vcxproj /p:Configuration=Debug /p:Platform=x64");
    Console.Error.WriteLine($"[SmokeTest] Then copy SnoopWPF.Agent.Bootstrap.dll to:");
    Console.Error.WriteLine($"[SmokeTest]   {exeDir}");
    return 2;  // SKIP — not a failure, just not built yet
}

Console.WriteLine($"[SmokeTest] Loading bootstrap DLL: {bootstrapDll}");
var hBootstrap = NativeMethods.LoadLibraryW(bootstrapDll);
if (hBootstrap == IntPtr.Zero)
{
    int err = Marshal.GetLastWin32Error();
    Console.Error.WriteLine($"[SmokeTest] FAIL: LoadLibrary failed — Win32 error {err}");
    return 1;
}
Console.WriteLine($"[SmokeTest] PASS: bootstrap DLL loaded at 0x{hBootstrap:X}");

// ---------------------------------------------------------------------------
// Step 3: Resolve the exported SnoopAgentStart function.
// ---------------------------------------------------------------------------
var pfnSnoopAgentStart = NativeMethods.GetProcAddress(hBootstrap, "SnoopAgentStart");
if (pfnSnoopAgentStart == IntPtr.Zero)
{
    Console.Error.WriteLine("[SmokeTest] FAIL: SnoopAgentStart export not found.");
    NativeMethods.FreeLibrary(hBootstrap);
    return 1;
}
Console.WriteLine($"[SmokeTest] PASS: SnoopAgentStart export at 0x{pfnSnoopAgentStart:X}");

// ---------------------------------------------------------------------------
// Step 4: Call SnoopAgentStart with nullptr (smoke-test mode).
//
// The bootstrap DLL will:
//   - Verify hostfxr.dll is loaded  (should succeed — we verified above)
//   - Resolve hostfxr proc addresses
//   - Call hostfxr_initialize_for_runtime_config with this exe's runtimeconfig
//   - Obtain load_assembly_and_get_function_pointer
//   - Load SnoopWPF.Agent.Injection.dll (must be alongside this exe)
//   - Call SnoopAgentUnmanagedEntry.Start with empty path
//   - Return INJECT_OK (0)
//
// Note: SnoopWPF.Agent.Injection.dll must be present next to the smoke test exe.
// ---------------------------------------------------------------------------

// Set up debug output listener to capture OutputDebugString from the bootstrap.
var dbgListener = new TextWriterTraceListener(Console.Out);
Trace.Listeners.Add(dbgListener);

Console.WriteLine("[SmokeTest] Calling SnoopAgentStart (smoke-test mode, param=null)...");

var sw = Stopwatch.StartNew();
var fn = Marshal.GetDelegateForFunctionPointer<SnoopAgentStartDelegate>(pfnSnoopAgentStart);
uint result = fn(IntPtr.Zero);
sw.Stop();

Console.WriteLine($"[SmokeTest] SnoopAgentStart returned: 0x{result:X8} in {sw.ElapsedMilliseconds}ms");

NativeMethods.FreeLibrary(hBootstrap);

// ---------------------------------------------------------------------------
// Step 5: Interpret result codes.
// ---------------------------------------------------------------------------
const uint INJECT_OK            = 0x00000000u;
const uint INJECT_WRONG_RUNTIME = 0xE0010001u;
const uint INJECT_NO_DELEGATE   = 0xE0010002u;
const uint INJECT_INIT_FAILED   = 0xE0010003u;
const uint INJECT_LOAD_FAILED   = 0xE0010004u;
const uint INJECT_CALL_FAILED   = 0xE0010005u;

switch (result)
{
    case INJECT_OK:
        Console.WriteLine("[SmokeTest] PASS: hostfxr bootstrap chain WORKS on CoreCLR (net10.0-windows).");
        Console.WriteLine("[SmokeTest] Phase B2 (Mode 3 injection) is UNBLOCKED — proceed with bd-1a9.33.");
        return 0;

    case INJECT_WRONG_RUNTIME:
        Console.Error.WriteLine("[SmokeTest] FAIL: INJECT_WRONG_RUNTIME — hostfxr.dll not found.");
        Console.Error.WriteLine("[SmokeTest] This should not happen in a net10 process. Check PATH.");
        return 1;

    case INJECT_NO_DELEGATE:
        Console.Error.WriteLine("[SmokeTest] FAIL: INJECT_NO_DELEGATE — hostfxr proc lookup failed.");
        Console.Error.WriteLine("[SmokeTest] hostfxr API mismatch — verify hostfxr.h version.");
        return 1;

    case INJECT_INIT_FAILED:
        Console.Error.WriteLine("[SmokeTest] FAIL: INJECT_INIT_FAILED — hostfxr_initialize_for_runtime_config failed.");
        Console.Error.WriteLine("[SmokeTest] runtimeconfig.json may not exist alongside the exe.");
        Console.Error.WriteLine($"[SmokeTest] Expected: {Path.ChangeExtension(Environment.ProcessPath!, ".runtimeconfig.json")}");
        return 1;

    case INJECT_LOAD_FAILED:
        Console.Error.WriteLine("[SmokeTest] FAIL: INJECT_LOAD_FAILED — load_assembly_and_get_function_pointer failed.");
        Console.Error.WriteLine("[SmokeTest] SnoopWPF.Agent.Injection.dll may be missing or the type/method name wrong.");
        Console.Error.WriteLine($"[SmokeTest] Expected assembly alongside exe: {Path.Combine(exeDir, "SnoopWPF.Agent.Injection.dll")}");
        return 1;

    case INJECT_CALL_FAILED:
        Console.Error.WriteLine("[SmokeTest] FAIL: INJECT_CALL_FAILED — managed entry returned non-zero.");
        return 1;

    default:
        Console.Error.WriteLine($"[SmokeTest] FAIL: Unknown result code 0x{result:X8}.");
        return 1;
}

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
delegate uint SnoopAgentStartDelegate(IntPtr pParam);

static class NativeMethods
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadLibraryW(string lpLibFileName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FreeLibrary(IntPtr hModule);
}
