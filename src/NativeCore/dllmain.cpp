#include <windows.h>
#include "Shared/AetherTelemetry.h"
#include "Shared/Config.h"
#include "DXGI/DXGIProxy.h"
#include "Streamline/StreamlineProxy.h"

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved)
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
        DisableThreadLibraryCalls(hModule);
        AetherConfig::Get().Load();
        AetherTelemetryServer::Get().Initialize();
        DXGIProxy::Initialize();
        StreamlineProxy::Initialize();
        break;

    case DLL_PROCESS_DETACH:
        StreamlineProxy::Shutdown();
        DXGIProxy::Shutdown();
        AetherTelemetryServer::Get().Shutdown();
        break;
    }
    return TRUE;
}
