#include "PayloadEntry.h"
#include "DXGI/CadenceEngine.h"
#include "DXGI/AntiLag2Bridge.h"
#include "Telemetry/TelemetryCore.h"
#include "Shared/Config.h"
#include "Vendor/MinHook/MinHook.h"
#include <d3d11.h>
#include <d3d12.h>
#include <dxgi1_6.h>
#include <windows.h>
#include <atomic>
#include <mutex>
#include <cstdint>
#include <cstdio>

typedef HRESULT(WINAPI *IDXGISwapChain_Present_t)(IDXGISwapChain *pSwapChain, UINT SyncInterval, UINT Flags);
typedef HRESULT(WINAPI *IDXGISwapChain1_Present1_t)(IDXGISwapChain1 *pSwapChain, UINT SyncInterval, UINT PresentFlags, const DXGI_PRESENT_PARAMETERS *pPresentParameters);
typedef HRESULT(WINAPI *IDXGISwapChain_ResizeBuffers_t)(IDXGISwapChain *pSwapChain, UINT BufferCount, UINT Width, UINT Height, DXGI_FORMAT NewFormat, UINT SwapChainFlags);
typedef HRESULT(WINAPI *IDXGIFactory_CreateSwapChain_t)(IDXGIFactory *pFactory, IUnknown *pDevice, DXGI_SWAP_CHAIN_DESC *pDesc, IDXGISwapChain **ppSwapChain);
typedef HRESULT(WINAPI *IDXGIFactory2_CreateSwapChainForHwnd_t)(IDXGIFactory2 *pFactory, IUnknown *pDevice, HWND hWnd, const DXGI_SWAP_CHAIN_DESC1 *pDesc, const DXGI_SWAP_CHAIN_FULLSCREEN_DESC *pFullscreenDesc, IDXGIOutput *pRestrictToOutput, IDXGISwapChain1 **ppSwapChain);
typedef HRESULT(WINAPI *CreateDXGIFactory_t)(REFIID riid, void **ppFactory);
typedef HRESULT(WINAPI *CreateDXGIFactory1_t)(REFIID riid, void **ppFactory);
typedef HRESULT(WINAPI *CreateDXGIFactory2_t)(UINT Flags, REFIID riid, void **ppFactory);

static IDXGISwapChain_Present_t Original_Present = nullptr;
static IDXGISwapChain1_Present1_t Original_Present1 = nullptr;
static IDXGISwapChain_ResizeBuffers_t Original_ResizeBuffers = nullptr;
static IDXGIFactory_CreateSwapChain_t Original_CreateSwapChain = nullptr;
static IDXGIFactory2_CreateSwapChainForHwnd_t Original_CreateSwapChainForHwnd = nullptr;
static CreateDXGIFactory_t Original_CreateDXGIFactory = nullptr;
static CreateDXGIFactory1_t Original_CreateDXGIFactory1 = nullptr;
static CreateDXGIFactory2_t Original_CreateDXGIFactory2 = nullptr;

static ID3D12CommandQueue* g_pD3D12CommandQueue = nullptr;
static std::atomic<bool> g_swapChainHooked{false};
static std::atomic<bool> g_factoryHooked{false};

static void LogDebug(const char* fmt, ...) {
    char buf[512];
    va_list args;
    va_start(args, fmt);
    vsnprintf(buf, sizeof(buf), fmt, args);
    va_end(args);
    OutputDebugStringA(buf);
}

static void SafeCaptureD3D12Context(IUnknown* pDeviceOrQueue) {
    if (!pDeviceOrQueue) return;

    ID3D12CommandQueue* pQueue = nullptr;
    if (SUCCEEDED(pDeviceOrQueue->QueryInterface(IID_PPV_ARGS(&pQueue))) && pQueue) {
        if (g_pD3D12CommandQueue && g_pD3D12CommandQueue != pQueue) {
            g_pD3D12CommandQueue->Release();
        }
        g_pD3D12CommandQueue = pQueue;
        LogDebug("[AetherPulse] Injected Payload: Captured ID3D12CommandQueue.\n");
    }
}

static HRESULT WINAPI Hooked_Present(IDXGISwapChain *pSwapChain, UINT SyncInterval, UINT Flags) {
    TelemetryCore::Get().RecordPresent();

    const auto& config = AetherConfig::Get();
    if (pSwapChain && config.pacing.enableAntiLag2) {
        AntiLag2Bridge::Get().TagSwapChain(pSwapChain, true, config.pacing.targetFpsCap);
    }

    if (config.pacing.enablePacing) {
        CadenceEngine::Get().SetTargetFPS(config.pacing.targetFpsCap);
        CadenceEngine::Get().OnPresentPacing(g_pD3D12CommandQueue);
    }

    if (Original_Present) {
        return Original_Present(pSwapChain, SyncInterval, Flags);
    }
    return S_OK;
}

static HRESULT WINAPI Hooked_Present1(IDXGISwapChain1 *pSwapChain, UINT SyncInterval, UINT PresentFlags, const DXGI_PRESENT_PARAMETERS *pPresentParameters) {
    TelemetryCore::Get().RecordPresent();

    const auto& config = AetherConfig::Get();
    if (pSwapChain && config.pacing.enableAntiLag2) {
        AntiLag2Bridge::Get().TagSwapChain(pSwapChain, true, config.pacing.targetFpsCap);
    }

    if (config.pacing.enablePacing) {
        CadenceEngine::Get().SetTargetFPS(config.pacing.targetFpsCap);
        CadenceEngine::Get().OnPresentPacing(g_pD3D12CommandQueue);
    }

    if (Original_Present1) {
        return Original_Present1(pSwapChain, SyncInterval, PresentFlags, pPresentParameters);
    }
    return S_OK;
}

static HRESULT WINAPI Hooked_ResizeBuffers(IDXGISwapChain *pSwapChain, UINT BufferCount, UINT Width, UINT Height, DXGI_FORMAT NewFormat, UINT SwapChainFlags) {
    const auto& config = AetherConfig::Get();
    if (config.pacing.enablePacing) {
        CadenceEngine::Get().SetTargetFPS(config.pacing.targetFpsCap);
    }
    if (Original_ResizeBuffers) {
        return Original_ResizeBuffers(pSwapChain, BufferCount, Width, Height, NewFormat, SwapChainFlags);
    }
    return S_OK;
}

static void HookSwapChainInstance(IDXGISwapChain* pSwapChain) {
    if (!pSwapChain) return;
    if (g_swapChainHooked.exchange(true)) return;

    void** pVMT = *reinterpret_cast<void***>(pSwapChain);
    if (pVMT && pVMT[8]) {
        MH_CreateHook(pVMT[8], reinterpret_cast<LPVOID>(&Hooked_Present), reinterpret_cast<LPVOID*>(&Original_Present));
        MH_EnableHook(pVMT[8]);
        LogDebug("[AetherPulse] Injected Payload: Hooked IDXGISwapChain::Present (Index 8).\n");
    }
    if (pVMT && pVMT[14]) {
        MH_CreateHook(pVMT[14], reinterpret_cast<LPVOID>(&Hooked_ResizeBuffers), reinterpret_cast<LPVOID*>(&Original_ResizeBuffers));
        MH_EnableHook(pVMT[14]);
        LogDebug("[AetherPulse] Injected Payload: Hooked IDXGISwapChain::ResizeBuffers (Index 14).\n");
    }

    IDXGISwapChain1* pSwapChain1 = nullptr;
    if (SUCCEEDED(pSwapChain->QueryInterface(__uuidof(IDXGISwapChain1), reinterpret_cast<void**>(&pSwapChain1))) && pSwapChain1) {
        void** pVMT1 = *reinterpret_cast<void***>(pSwapChain1);
        if (pVMT1 && pVMT1[22]) {
            MH_CreateHook(pVMT1[22], reinterpret_cast<LPVOID>(&Hooked_Present1), reinterpret_cast<LPVOID*>(&Original_Present1));
            MH_EnableHook(pVMT1[22]);
            LogDebug("[AetherPulse] Injected Payload: Hooked IDXGISwapChain1::Present1 (Index 22).\n");
        }
        pSwapChain1->Release();
    }
}

static HRESULT WINAPI Hooked_CreateSwapChain(IDXGIFactory *pFactory, IUnknown *pDevice, DXGI_SWAP_CHAIN_DESC *pDesc, IDXGISwapChain **ppSwapChain) {
    HRESULT hr = Original_CreateSwapChain ? Original_CreateSwapChain(pFactory, pDevice, pDesc, ppSwapChain) : E_FAIL;
    if (SUCCEEDED(hr) && ppSwapChain && *ppSwapChain) {
        HookSwapChainInstance(*ppSwapChain);
        if (pDevice) SafeCaptureD3D12Context(pDevice);
    }
    return hr;
}

static HRESULT WINAPI Hooked_CreateSwapChainForHwnd(IDXGIFactory2 *pFactory, IUnknown *pDevice, HWND hWnd, const DXGI_SWAP_CHAIN_DESC1 *pDesc, const DXGI_SWAP_CHAIN_FULLSCREEN_DESC *pFullscreenDesc, IDXGIOutput *pRestrictToOutput, IDXGISwapChain1 **ppSwapChain) {
    HRESULT hr = Original_CreateSwapChainForHwnd ? Original_CreateSwapChainForHwnd(pFactory, pDevice, hWnd, pDesc, pFullscreenDesc, pRestrictToOutput, ppSwapChain) : E_FAIL;
    if (SUCCEEDED(hr) && ppSwapChain && *ppSwapChain) {
        HookSwapChainInstance(*ppSwapChain);
        if (pDevice) SafeCaptureD3D12Context(pDevice);
    }
    return hr;
}

static void HookFactoryInstance(IDXGIFactory* pFactory) {
    if (!pFactory) return;
    if (g_factoryHooked.exchange(true)) return;

    void** pVMT = *reinterpret_cast<void***>(pFactory);
    if (pVMT && pVMT[10]) {
        MH_CreateHook(pVMT[10], reinterpret_cast<LPVOID>(&Hooked_CreateSwapChain), reinterpret_cast<LPVOID*>(&Original_CreateSwapChain));
        MH_EnableHook(pVMT[10]);
        LogDebug("[AetherPulse] Injected Payload: Hooked IDXGIFactory::CreateSwapChain (Index 10).\n");
    }

    IDXGIFactory2* pFactory2 = nullptr;
    if (SUCCEEDED(pFactory->QueryInterface(__uuidof(IDXGIFactory2), reinterpret_cast<void**>(&pFactory2))) && pFactory2) {
        void** pVMT2 = *reinterpret_cast<void***>(pFactory2);
        if (pVMT2 && pVMT2[15]) {
            MH_CreateHook(pVMT2[15], reinterpret_cast<LPVOID>(&Hooked_CreateSwapChainForHwnd), reinterpret_cast<LPVOID*>(&Original_CreateSwapChainForHwnd));
            MH_EnableHook(pVMT2[15]);
            LogDebug("[AetherPulse] Injected Payload: Hooked IDXGIFactory2::CreateSwapChainForHwnd (Index 15).\n");
        }
        pFactory2->Release();
    }
}

static HRESULT WINAPI Hooked_CreateDXGIFactory(REFIID riid, void **ppFactory) {
    HRESULT hr = Original_CreateDXGIFactory ? Original_CreateDXGIFactory(riid, ppFactory) : E_FAIL;
    if (SUCCEEDED(hr) && ppFactory && *ppFactory) {
        HookFactoryInstance(static_cast<IDXGIFactory*>(*ppFactory));
    }
    return hr;
}

static HRESULT WINAPI Hooked_CreateDXGIFactory1(REFIID riid, void **ppFactory) {
    HRESULT hr = Original_CreateDXGIFactory1 ? Original_CreateDXGIFactory1(riid, ppFactory) : E_FAIL;
    if (SUCCEEDED(hr) && ppFactory && *ppFactory) {
        HookFactoryInstance(static_cast<IDXGIFactory*>(*ppFactory));
    }
    return hr;
}

static HRESULT WINAPI Hooked_CreateDXGIFactory2(UINT Flags, REFIID riid, void **ppFactory) {
    HRESULT hr = Original_CreateDXGIFactory2 ? Original_CreateDXGIFactory2(Flags, riid, ppFactory) : E_FAIL;
    if (SUCCEEDED(hr) && ppFactory && *ppFactory) {
        HookFactoryInstance(static_cast<IDXGIFactory*>(*ppFactory));
    }
    return hr;
}

static void ProbeAndHookActiveSwapChain() {
    WNDCLASSEXW wc = { sizeof(WNDCLASSEXW), CS_CLASSDC, DefWindowProcW, 0L, 0L, GetModuleHandleW(NULL), NULL, NULL, NULL, NULL, L"AetherProbeWindow", NULL };
    RegisterClassExW(&wc);
    HWND hWnd = CreateWindowW(wc.lpszClassName, L"AetherProbe", WS_OVERLAPPEDWINDOW, 0, 0, 100, 100, NULL, NULL, wc.hInstance, NULL);

    if (hWnd) {
        DXGI_SWAP_CHAIN_DESC sd = {};
        sd.BufferCount = 1;
        sd.BufferDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
        sd.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
        sd.OutputWindow = hWnd;
        sd.SampleDesc.Count = 1;
        sd.Windowed = TRUE;

        D3D_FEATURE_LEVEL featureLevel;
        ID3D11Device* pD3DDevice = nullptr;
        ID3D11DeviceContext* pContext = nullptr;
        IDXGISwapChain* pProbeSwapChain = nullptr;

        HMODULE hD3D11 = LoadLibraryW(L"d3d11.dll");
        if (hD3D11) {
            typedef HRESULT(WINAPI *PFN_D3D11_CREATE_DEVICE_AND_SWAP_CHAIN)(
                IDXGIAdapter*, D3D_DRIVER_TYPE, HMODULE, UINT,
                const D3D_FEATURE_LEVEL*, UINT, UINT,
                const DXGI_SWAP_CHAIN_DESC*, IDXGISwapChain**,
                ID3D11Device**, D3D_FEATURE_LEVEL*, ID3D11DeviceContext**);

            auto pfnCreate = reinterpret_cast<PFN_D3D11_CREATE_DEVICE_AND_SWAP_CHAIN>(GetProcAddress(hD3D11, "D3D11CreateDeviceAndSwapChain"));
            if (pfnCreate) {
                HRESULT hr = pfnCreate(NULL, D3D_DRIVER_TYPE_HARDWARE, NULL, 0, NULL, 0, D3D11_SDK_VERSION, &sd, &pProbeSwapChain, &pD3DDevice, &featureLevel, &pContext);
                if (SUCCEEDED(hr) && pProbeSwapChain) {
                    HookSwapChainInstance(pProbeSwapChain);
                    pProbeSwapChain->Release();
                }
                if (pContext) pContext->Release();
                if (pD3DDevice) pD3DDevice->Release();
            }
        }

        DestroyWindow(hWnd);
        UnregisterClassW(wc.lpszClassName, wc.hInstance);
    }
}

static DWORD WINAPI PayloadWorkerThread(LPVOID lpParam) {
    Sleep(50);

    AetherConfig::Get().Load();
    TelemetryCore::Get().Initialize();

    if (MH_Initialize() == MH_OK || MH_Initialize() == MH_ERROR_ALREADY_INITIALIZED) {
        // 1. Hook DXGI Factory Creation exports on system dxgi.dll
        HMODULE hDXGI = GetModuleHandleW(L"dxgi.dll");
        if (!hDXGI) hDXGI = LoadLibraryW(L"dxgi.dll");

        if (hDXGI) {
            FARPROC pF0 = GetProcAddress(hDXGI, "CreateDXGIFactory");
            FARPROC pF1 = GetProcAddress(hDXGI, "CreateDXGIFactory1");
            FARPROC pF2 = GetProcAddress(hDXGI, "CreateDXGIFactory2");

            if (pF0) MH_CreateHook(pF0, reinterpret_cast<LPVOID>(&Hooked_CreateDXGIFactory), reinterpret_cast<LPVOID*>(&Original_CreateDXGIFactory));
            if (pF1) MH_CreateHook(pF1, reinterpret_cast<LPVOID>(&Hooked_CreateDXGIFactory1), reinterpret_cast<LPVOID*>(&Original_CreateDXGIFactory1));
            if (pF2) MH_CreateHook(pF2, reinterpret_cast<LPVOID>(&Hooked_CreateDXGIFactory2), reinterpret_cast<LPVOID*>(&Original_CreateDXGIFactory2));
        }

        // 2. Obtain and hook SwapChain Present VTable directly
        ProbeAndHookActiveSwapChain();

        MH_EnableHook(MH_ALL_HOOKS);
        LogDebug("[AetherPulse] Injected Payload: Global hooks enabled successfully.\n");
    }

    return 0;
}

namespace AetherPulse
{
    void StartPayloadWorker() {
        CloseHandle(CreateThread(nullptr, 0, PayloadWorkerThread, nullptr, 0, nullptr));
    }

    void StopPayloadWorker() {
        TelemetryCore::Get().Shutdown();
        MH_DisableHook(MH_ALL_HOOKS);
        MH_Uninitialize();
    }
}
