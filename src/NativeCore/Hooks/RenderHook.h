#pragma once
#include <windows.h>

namespace RenderHook {
    DWORD WINAPI InitHookThread(LPVOID);
    void Shutdown();
}
