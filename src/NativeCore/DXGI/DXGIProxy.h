#pragma once

#include <windows.h>
#include <dxgi.h>
#include <dxgi1_6.h>

namespace DXGIProxy
{
    bool Initialize();
    void Shutdown();

    // MinHook Hook targets and functions
    bool HookSwapChain(IDXGISwapChain* pSwapChain);

    // SwapChain Present Detours
    HRESULT WINAPI DetourPresent(
        IDXGISwapChain* pSwapChain,
        UINT SyncInterval,
        UINT Flags
    );

    HRESULT WINAPI DetourPresent1(
        IDXGISwapChain1* pSwapChain1,
        UINT SyncInterval,
        UINT PresentFlags,
        const DXGI_PRESENT_PARAMETERS* pPresentParameters
    );
}

// Original system DXGI function forwarders
extern "C" {
    HRESULT WINAPI ProxyCreateDXGIFactory(REFIID riid, void** ppFactory);
    HRESULT WINAPI ProxyCreateDXGIFactory1(REFIID riid, void** ppFactory);
    HRESULT WINAPI ProxyCreateDXGIFactory2(UINT Flags, REFIID riid, void** ppFactory);
    HRESULT WINAPI ProxyDXGID3D10CreateDevice(HMODULE hModule, IDXGIFactory* pFactory, IDXGIAdapter* pAdapter, UINT Flags, void* pUnknown, void** ppDevice);
    HRESULT WINAPI ProxyDXGID3D10RegisterLayers(const void* pLayers, UINT NumLayers);
    HRESULT WINAPI ProxyDXGIGetDebugInterface1(UINT Flags, REFIID riid, void** ppDebug);
}
