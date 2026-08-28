#include <windows.h>
#include <atomic>
#include "Hooks/RenderHook.h"
#include "Logging/CrashHandler.h"

static std::atomic<bool> g_ProcessInitialized{false};

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved) {
    if (ul_reason_for_call == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(hModule);
        if (!g_ProcessInitialized.exchange(true)) {
            CrashHandler::Install();
            HANDLE hThread = CreateThread(nullptr, 0, RenderHook::InitHookThread, nullptr, 0, nullptr);
            if (hThread) CloseHandle(hThread);
        }
    } else if (ul_reason_for_call == DLL_PROCESS_DETACH && lpReserved == nullptr) {
        if (g_ProcessInitialized.exchange(false)) {
            RenderHook::Shutdown();
            CrashHandler::Uninstall();
        }
    }
    return TRUE;
}
