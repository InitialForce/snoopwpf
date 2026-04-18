// SnoopAgentStart.cpp — CoreCLR bootstrap via hostfxr/nethost
//
// Path A (bd-1a9.32): use hostfxr_get_runtime_delegate to obtain
// load_assembly_and_get_function_pointer, then call the managed
// [UnmanagedCallersOnly] entry in SnoopWPF.Agent.Injection.
//
// This runs on a worker thread — never on the loader-lock thread.
//
// Required headers ship with:
//   Microsoft.NETCore.App.Host.win-x64 (NuGet / dotnet packs)
//
// Error codes (returned as DWORD to the injector):
//   0x00000000  success — managed entry was invoked
//   0xE0010001  INJECT_WRONG_RUNTIME  — hostfxr.dll absent (Framework target)
//   0xE0010002  INJECT_NO_DELEGATE    — hostfxr proc lookup failed
//   0xE0010003  INJECT_INIT_FAILED    — hostfxr_initialize_for_runtime_config failed
//   0xE0010004  INJECT_LOAD_FAILED    — load_assembly_and_get_function_pointer failed
//   0xE0010005  INJECT_CALL_FAILED    — managed entry returned non-zero

#include "pch.h"

// nethost + hostfxr headers come from Microsoft.NETCore.App.Host.win-x64 NuGet pack.
// The vcxproj AdditionalIncludeDirectories points at them.
#include <nethost.h>
#include <hostfxr.h>
#include <coreclr_delegates.h>

// Inline file-exists helper — avoids shlwapi dependency.
static bool FileExists(const wchar_t* path)
{
    DWORD attrs = ::GetFileAttributesW(path);
    return (attrs != INVALID_FILE_ATTRIBUTES) && !(attrs & FILE_ATTRIBUTE_DIRECTORY);
}

// -----------------------------------------------------------------------
// Error codes exported to the injector
// -----------------------------------------------------------------------
#define INJECT_OK               0x00000000ul
#define INJECT_WRONG_RUNTIME    0xE0010001ul
#define INJECT_NO_DELEGATE      0xE0010002ul
#define INJECT_INIT_FAILED      0xE0010003ul
#define INJECT_LOAD_FAILED      0xE0010004ul
#define INJECT_CALL_FAILED      0xE0010005ul

// -----------------------------------------------------------------------
// Managed entry signature (must match [UnmanagedCallersOnly] declaration)
// C# `nint` maps to INT_PTR (pointer-sized integer) on the native side.
// On x64 that is int64_t / __int64.
// -----------------------------------------------------------------------
typedef int (STDMETHODCALLTYPE* SnoopAgentManagedEntry)(
    INT_PTR        settingsFilePathPtr,  // C# nint — pointer to wchar_t string
    int            settingsFilePathLength);

// -----------------------------------------------------------------------
// SnoopAgentStart — exported entry point
//
// Parameter: LPVOID pParam — pointer to a null-terminated wide string
//   containing the settings file path, or nullptr for smoke-test mode.
//
// Returns DWORD result code (see above). The injector (bd-1a9.33) reads
// this via the remote thread exit code.
// -----------------------------------------------------------------------
extern "C" __declspec(dllexport) DWORD WINAPI SnoopAgentStart(LPVOID pParam)
{
    // ------------------------------------------------------------------
    // Step 1: Check that hostfxr.dll is already loaded in this process.
    // In a running CoreCLR app, hostfxr.dll is ALWAYS loaded.
    // If it is absent, the target is a .NET Framework process — abort.
    // ------------------------------------------------------------------
    HMODULE hHostFxr = ::GetModuleHandleW(L"hostfxr.dll");
    if (!hHostFxr)
    {
        // hostfxr.dll not loaded — this is a .NET Framework target.
        // Return INJECT_WRONG_RUNTIME so the broker can surface a clear error.
        ::OutputDebugStringW(L"[SnoopBootstrap] hostfxr.dll not found — target is not CoreCLR. "
                             L"Returning INJECT_WRONG_RUNTIME.\n");
        return INJECT_WRONG_RUNTIME;
    }

    // ------------------------------------------------------------------
    // Step 2: Resolve hostfxr_get_runtime_delegate from the already-loaded
    // hostfxr.dll. We do NOT LoadLibrary — we use the module already in
    // the target process, which is the one managing the running CLR.
    // ------------------------------------------------------------------
    auto pfnGetRuntimeDelegate = reinterpret_cast<hostfxr_get_runtime_delegate_fn>(
        ::GetProcAddress(hHostFxr, "hostfxr_get_runtime_delegate"));

    auto pfnInitForRuntimeConfig = reinterpret_cast<hostfxr_initialize_for_runtime_config_fn>(
        ::GetProcAddress(hHostFxr, "hostfxr_initialize_for_runtime_config"));

    auto pfnClose = reinterpret_cast<hostfxr_close_fn>(
        ::GetProcAddress(hHostFxr, "hostfxr_close"));

    if (!pfnGetRuntimeDelegate || !pfnInitForRuntimeConfig || !pfnClose)
    {
        ::OutputDebugStringW(L"[SnoopBootstrap] GetProcAddress failed for hostfxr functions.\n");
        return INJECT_NO_DELEGATE;
    }

    // ------------------------------------------------------------------
    // Step 3: Locate the runtimeconfig.json for the target executable.
    //
    // In a running CoreCLR app the .runtimeconfig.json was already
    // consumed at startup.  hostfxr_initialize_for_runtime_config()
    // accepts it as a hint to create an additional host context that
    // can resolve the load_assembly_and_get_function_pointer delegate.
    //
    // We derive the runtimeconfig path from the host exe path:
    //   <exeName>.runtimeconfig.json
    //
    // If that file does not exist on disk (single-file publish, etc.),
    // we fall back to the bootstrap DLL's own sidecar runtimeconfig.
    // ------------------------------------------------------------------
    wchar_t exePath[MAX_PATH] = {};
    ::GetModuleFileNameW(nullptr, exePath, MAX_PATH);

    // Replace ".exe" suffix with ".runtimeconfig.json"
    std::wstring rcPath(exePath);
    auto dotPos = rcPath.rfind(L'.');
    if (dotPos != std::wstring::npos)
    {
        rcPath = rcPath.substr(0, dotPos);
    }
    rcPath += L".runtimeconfig.json";

    // Fallback: sidecar config next to this DLL.
    if (!FileExists(rcPath.c_str()))
    {
        wchar_t dllPath[MAX_PATH] = {};
        HMODULE hSelf = nullptr;
        ::GetModuleHandleExW(
            GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            reinterpret_cast<LPCWSTR>(&SnoopAgentStart),
            &hSelf);
        ::GetModuleFileNameW(hSelf, dllPath, MAX_PATH);

        std::wstring dllDir(dllPath);
        auto lastSlash = dllDir.rfind(L'\\');
        if (lastSlash != std::wstring::npos)
        {
            dllDir = dllDir.substr(0, lastSlash + 1);
        }
        rcPath = dllDir + L"SnoopWPF.Agent.Bootstrap.runtimeconfig.json";
    }

    ::OutputDebugStringW((std::wstring(L"[SnoopBootstrap] Using runtimeconfig: ") + rcPath + L"\n").c_str());

    // ------------------------------------------------------------------
    // Step 4: Initialize a secondary host context.
    // This creates a new "additional" ALC-based context inside the
    // already-running CoreCLR — it does NOT start a second runtime.
    // ------------------------------------------------------------------
    hostfxr_handle hContext = nullptr;
    int32_t rc = pfnInitForRuntimeConfig(rcPath.c_str(), nullptr, &hContext);

    // rc == 0 (Success) or 1 (Success_HostAlreadyInitialized) are both acceptable.
    // Any negative value is a failure.
    if (rc < 0)
    {
        wchar_t msg[256];
        swprintf_s(msg, L"[SnoopBootstrap] hostfxr_initialize_for_runtime_config failed: 0x%08X\n",
                   static_cast<unsigned>(rc));
        ::OutputDebugStringW(msg);
        return INJECT_INIT_FAILED;
    }

    // ------------------------------------------------------------------
    // Step 5: Obtain the load_assembly_and_get_function_pointer delegate.
    // ------------------------------------------------------------------
    load_assembly_and_get_function_pointer_fn pfnLoadAssembly = nullptr;
    rc = pfnGetRuntimeDelegate(
        hContext,
        hdt_load_assembly_and_get_function_pointer,
        reinterpret_cast<void**>(&pfnLoadAssembly));

    pfnClose(hContext);  // Always close the context handle.

    if (rc != 0 || !pfnLoadAssembly)
    {
        wchar_t msg[256];
        swprintf_s(msg, L"[SnoopBootstrap] hostfxr_get_runtime_delegate failed: 0x%08X\n",
                   static_cast<unsigned>(rc));
        ::OutputDebugStringW(msg);
        return INJECT_LOAD_FAILED;
    }

    // ------------------------------------------------------------------
    // Step 6: Resolve the bootstrap DLL's directory — we load
    // SnoopWPF.Agent.Injection.dll from the same location.
    // ------------------------------------------------------------------
    wchar_t selfDllPath[MAX_PATH] = {};
    HMODULE hSelf2 = nullptr;
    ::GetModuleHandleExW(
        GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(&SnoopAgentStart),
        &hSelf2);
    ::GetModuleFileNameW(hSelf2, selfDllPath, MAX_PATH);

    std::wstring agentDir(selfDllPath);
    auto lastSlash2 = agentDir.rfind(L'\\');
    if (lastSlash2 != std::wstring::npos)
    {
        agentDir = agentDir.substr(0, lastSlash2 + 1);
    }
    std::wstring injectionDll = agentDir + L"SnoopWPF.Agent.Injection.dll";

    ::OutputDebugStringW((std::wstring(L"[SnoopBootstrap] Loading managed assembly: ") + injectionDll + L"\n").c_str());

    // ------------------------------------------------------------------
    // Step 7: Load the managed assembly and get a pointer to the
    // [UnmanagedCallersOnly] entry point.
    //
    // The managed method signature must match SnoopAgentManagedEntry:
    //   static int Start(nint settingsFilePathPtr, int length)
    // declared in SnoopWPF.Agent.Injection.SnoopAgentUnmanagedEntry.
    // ------------------------------------------------------------------
    SnoopAgentManagedEntry pfnManagedEntry = nullptr;
    rc = pfnLoadAssembly(
        injectionDll.c_str(),
        L"SnoopWPF.Agent.Injection.SnoopAgentUnmanagedEntry, SnoopWPF.Agent.Injection",
        L"Start",
        UNMANAGEDCALLERSONLY_METHOD,  // use [UnmanagedCallersOnly] marker
        nullptr,
        reinterpret_cast<void**>(&pfnManagedEntry));

    if (rc != 0 || !pfnManagedEntry)
    {
        wchar_t msg[256];
        swprintf_s(msg, L"[SnoopBootstrap] load_assembly_and_get_function_pointer failed: 0x%08X\n",
                   static_cast<unsigned>(rc));
        ::OutputDebugStringW(msg);
        return INJECT_LOAD_FAILED;
    }

    // ------------------------------------------------------------------
    // Step 8: Invoke the managed entry.
    // Pass the settings file path as INT_PTR (matches C# nint parameter).
    // Smoke-test mode: pass nullptr → INT_PTR 0 with length 0.
    // ------------------------------------------------------------------
    const wchar_t* settingsPath = (pParam != nullptr)
        ? static_cast<const wchar_t*>(pParam)
        : nullptr;

    int settingsLen = settingsPath ? static_cast<int>(wcslen(settingsPath)) : 0;

    ::OutputDebugStringW(L"[SnoopBootstrap] Calling managed SnoopAgentUnmanagedEntry.Start\n");

    int managedResult = pfnManagedEntry(
        reinterpret_cast<INT_PTR>(settingsPath),
        settingsLen);

    if (managedResult != 0)
    {
        wchar_t msg[256];
        swprintf_s(msg, L"[SnoopBootstrap] Managed entry returned error: %d\n", managedResult);
        ::OutputDebugStringW(msg);
        return INJECT_CALL_FAILED;
    }

    ::OutputDebugStringW(L"[SnoopBootstrap] SnoopAgentStart completed successfully.\n");
    return INJECT_OK;
}
