#include "DXGIProxy.h"
#include "../FramePacer.h"
#include <dxgi1_4.h>

typedef HRESULT(WINAPI* PFN_Present)(IDXGISwapChain* This, UINT SyncInterval, UINT Flags);
typedef HRESULT(WINAPI* PFN_Present1)(IDXGISwapChain1* This, UINT SyncInterval, UINT Flags, const DXGI_PRESENT_PARAMETERS* pPresentParameters);

static PFN_Present  g_pfnOriginalPresent = nullptr;
static PFN_Present1 g_pfnOriginalPresent1 = nullptr;
static bool         g_inPresent = false;

HRESULT WINAPI Hooked_Present(IDXGISwapChain* This, UINT SyncInterval, UINT Flags)
{
    if (g_inPresent)
    {
        return g_pfnOriginalPresent ? g_pfnOriginalPresent(This, SyncInterval, Flags) : This->Present(SyncInterval, Flags);
    }

    g_inPresent = true;
    FSRFramePacer::GetInstance().OnBeforePresent(This, SyncInterval, Flags);

    HRESULT hr = g_pfnOriginalPresent ? g_pfnOriginalPresent(This, SyncInterval, Flags) : This->Present(SyncInterval, Flags);

    FSRFramePacer::GetInstance().OnAfterPresent(This, hr);
    g_inPresent = false;
    return hr;
}

HRESULT WINAPI Hooked_Present1(IDXGISwapChain1* This, UINT SyncInterval, UINT Flags, const DXGI_PRESENT_PARAMETERS* pPresentParameters)
{
    if (g_inPresent)
    {
        return g_pfnOriginalPresent1 ? g_pfnOriginalPresent1(This, SyncInterval, Flags, pPresentParameters) : This->Present1(SyncInterval, Flags, pPresentParameters);
    }

    g_inPresent = true;
    FSRFramePacer::GetInstance().OnBeforePresent(This, SyncInterval, Flags);

    HRESULT hr = g_pfnOriginalPresent1 ? g_pfnOriginalPresent1(This, SyncInterval, Flags, pPresentParameters) : This->Present1(SyncInterval, Flags, pPresentParameters);

    FSRFramePacer::GetInstance().OnAfterPresent(This, hr);
    g_inPresent = false;
    return hr;
}

void HookSwapChainVMT(IDXGISwapChain* pSwapChain)
{
    if (!pSwapChain) return;

    void** vtbl = *reinterpret_cast<void***>(pSwapChain);
    if (!vtbl) return;

    if (vtbl[8] != reinterpret_cast<void*>(Hooked_Present))
    {
        DWORD oldProtect = 0;
        if (VirtualProtect(&vtbl[8], sizeof(void*), PAGE_EXECUTE_READWRITE, &oldProtect))
        {
            g_pfnOriginalPresent = reinterpret_cast<PFN_Present>(vtbl[8]);
            vtbl[8] = reinterpret_cast<void*>(Hooked_Present);
            VirtualProtect(&vtbl[8], sizeof(void*), oldProtect, &oldProtect);
        }
    }

    IDXGISwapChain1* pSwapChain1 = nullptr;
    if (SUCCEEDED(pSwapChain->QueryInterface(IID_PPV_ARGS(&pSwapChain1))))
    {
        void** vtbl1 = *reinterpret_cast<void***>(pSwapChain1);
        if (vtbl1 && vtbl1[22] != reinterpret_cast<void*>(Hooked_Present1))
        {
            DWORD oldProtect = 0;
            if (VirtualProtect(&vtbl1[22], sizeof(void*), PAGE_EXECUTE_READWRITE, &oldProtect))
            {
                g_pfnOriginalPresent1 = reinterpret_cast<PFN_Present1>(vtbl1[22]);
                vtbl1[22] = reinterpret_cast<void*>(Hooked_Present1);
                VirtualProtect(&vtbl1[22], sizeof(void*), oldProtect, &oldProtect);
            }
        }
        pSwapChain1->Release();
    }
}

void HookSwapChain(IDXGISwapChain* pSwapChain)
{
    HookSwapChainVMT(pSwapChain);
}

void HookFactory(void* pFactory)
{
}

void InitializeDXGIProxyAndHooks()
{
}

void ShutdownDXGIProxyAndHooks()
{
}

extern "C" {
    __declspec(dllexport) HRESULT WINAPI CompatString(void* p1, void* p2, void* p3, void* p4) { return E_NOTIMPL; }
    __declspec(dllexport) HRESULT WINAPI CompatValue(void* p1, void* p2) { return E_NOTIMPL; }
    __declspec(dllexport) HRESULT WINAPI DXGICreateGlobalKeyedMutex(void* p1, void* p2) { return E_NOTIMPL; }
    __declspec(dllexport) HRESULT WINAPI DXGIOpenGlobalKeyedMutex(void* p1, void* p2) { return E_NOTIMPL; }
    __declspec(dllexport) HRESULT WINAPI SetAppCompatStringPointer(void* p1, void* p2) { return E_NOTIMPL; }
    __declspec(dllexport) HRESULT WINAPI UpdateOverlaySupport(void* p1) { return E_NOTIMPL; }
}

