// dllmain.cpp — SnoopWPF.Agent.Bootstrap
//
// DllMain MUST return immediately. No managed calls, no threads, no I/O.
// The exported SnoopAgentStart is the real entry — called from a worker thread
// spawned by the injector (bd-1a9.33) via CreateRemoteThread.
//
// Pattern: "no managed code in DllMain" — classic loader-lock safety rule.

#include "pch.h"

BOOL APIENTRY DllMain(HMODULE /*hModule*/, DWORD ul_reason_for_call, LPVOID /*lpReserved*/)
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}
