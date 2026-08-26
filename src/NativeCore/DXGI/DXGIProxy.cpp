#include "DXGIProxy.h"
#include "FramePacer.h"
#include "AntiLag2Bridge.h"
#include "../Vendor/MinHook/MinHook.h"
#include "../Shared/Config.h"
#include <mutex>
#include <unordered_set>
#include <d3d11.h>
#include <d3d12.h>

namespace
{
    HMODULE g_hRealDXGI = nullptr;

    typedef HRESULT(WINAPI* PFN_CreateDXGIFactory)(REFIID riid, void** ppFactory);
    typedef HRESULT(WINAPI* PFN_CreateDXGIFactory1)(REFIID riid, void** ppFactory);
    typedef HRESULT(WINAPI* PFN_CreateDXGIFactory2)(UINT Flags, REFIID riid, void** ppFactory);
    typedef HRESULT(WINAPI* PFN_DXGID3D10CreateDevice)(HMODULE hModule, IDXGIFactory* pFactory, IDXGIAdapter* pAdapter, UINT Flags, void* pUnknown, void** ppDevice);
    typedef HRESULT(WINAPI* PFN_DXGID3D10RegisterLayers)(const void* pLayers, UINT NumLayers);
    typedef HRESULT(WINAPI* PFN_DXGIGetDebugInterface1)(UINT Flags, REFIID riid, void** ppDebug);

    typedef HRESULT(WINAPI* PFN_DXGIDumpLiveObjects)(void);

    PFN_CreateDXGIFactory        g_RealCreateDXGIFactory = nullptr;
    PFN_CreateDXGIFactory1       g_RealCreateDXGIFactory1 = nullptr;
    PFN_CreateDXGIFactory2       g_RealCreateDXGIFactory2 = nullptr;
    PFN_DXGID3D10CreateDevice    g_RealDXGID3D10CreateDevice = nullptr;
    PFN_DXGID3D10RegisterLayers  g_RealDXGID3D10RegisterLayers = nullptr;
    PFN_DXGIGetDebugInterface1   g_RealDXGIGetDebugInterface1 = nullptr;
    PFN_DXGIDumpLiveObjects      g_RealDXGIDumpLiveObjects = nullptr;

    typedef HRESULT(WINAPI* PFN_Present)(IDXGISwapChain*, UINT, UINT);
    typedef HRESULT(WINAPI* PFN_Present1)(IDXGISwapChain1*, UINT, UINT, const DXGI_PRESENT_PARAMETERS*);
    typedef HRESULT(WINAPI* PFN_ResizeBuffers)(IDXGISwapChain*, UINT, UINT, UINT, DXGI_FORMAT, UINT);

    PFN_Present       g_OriginalPresent = nullptr;
    PFN_Present1      g_OriginalPresent1 = nullptr;
    PFN_ResizeBuffers g_OriginalResizeBuffers = nullptr;

    std::mutex g_hookMutex;
    std::unordered_set<void*> g_hookedVtables;

    void LoadSystemDXGI()
    {
        if (g_hRealDXGI) return;

        const auto& config = AetherConfig::Get();

        // 1. Check if an explicit OriginalDllPath is configured
        if (config.chaining.enableProxyChaining && !config.chaining.originalDllPath.empty())
        {
            g_hRealDXGI = LoadLibraryW(config.chaining.originalDllPath.c_str());
        }

        // 2. Check for local sibling proxy DLLs (ReShade, RenoDX, OptiScaler, or renamed chain)
        if (!g_hRealDXGI && config.chaining.enableProxyChaining)
        {
            const wchar_t* candidates[] = {
                L"dxgi_chain.dll",
                L"ReShade64.dll",
                L"OptiScaler.dll"
            };

            for (const auto* candidate : candidates)
            {
                if (GetFileAttributesW(candidate) != INVALID_FILE_ATTRIBUTES)
                {
                    g_hRealDXGI = LoadLibraryW(candidate);
                    if (g_hRealDXGI) break;
                }
            }
        }

        // 3. Fallback to native Windows System32 dxgi.dll
        if (!g_hRealDXGI)
        {
            wchar_t systemPath[MAX_PATH];
            GetSystemDirectoryW(systemPath, MAX_PATH);
            wcscat_s(systemPath, L"\\dxgi.dll");
            g_hRealDXGI = LoadLibraryW(systemPath);
        }

        if (g_hRealDXGI)
        {
            g_RealCreateDXGIFactory       = reinterpret_cast<PFN_CreateDXGIFactory>(GetProcAddress(g_hRealDXGI, "CreateDXGIFactory"));
            g_RealCreateDXGIFactory1      = reinterpret_cast<PFN_CreateDXGIFactory1>(GetProcAddress(g_hRealDXGI, "CreateDXGIFactory1"));
            g_RealCreateDXGIFactory2      = reinterpret_cast<PFN_CreateDXGIFactory2>(GetProcAddress(g_hRealDXGI, "CreateDXGIFactory2"));
            g_RealDXGID3D10CreateDevice   = reinterpret_cast<PFN_DXGID3D10CreateDevice>(GetProcAddress(g_hRealDXGI, "DXGID3D10CreateDevice"));
            g_RealDXGID3D10RegisterLayers = reinterpret_cast<PFN_DXGID3D10RegisterLayers>(GetProcAddress(g_hRealDXGI, "DXGID3D10RegisterLayers"));
            g_RealDXGIGetDebugInterface1  = reinterpret_cast<PFN_DXGIGetDebugInterface1>(GetProcAddress(g_hRealDXGI, "DXGIGetDebugInterface1"));
            g_RealDXGIDumpLiveObjects     = reinterpret_cast<PFN_DXGIDumpLiveObjects>(GetProcAddress(g_hRealDXGI, "DXGIDumpLiveObjects"));
        }
    }
}

namespace DXGIProxy
{
    HRESULT WINAPI DetourPresent(IDXGISwapChain* pSwapChain, UINT SyncInterval, UINT Flags)
    {
        FramePacer::Get().OnBeforePresent(pSwapChain, SyncInterval, Flags);
        AntiLag2Bridge::Get().MarkEndOfFrameRendering();
        HRESULT hr = g_OriginalPresent(pSwapChain, SyncInterval, Flags);
        FramePacer::Get().OnAfterPresent(pSwapChain, hr);
        return hr;
    }

    HRESULT WINAPI DetourPresent1(
        IDXGISwapChain1* pSwapChain1,
        UINT SyncInterval,
        UINT PresentFlags,
        const DXGI_PRESENT_PARAMETERS* pPresentParameters)
    {
        FramePacer::Get().OnBeforePresent(pSwapChain1, SyncInterval, PresentFlags);
        AntiLag2Bridge::Get().MarkEndOfFrameRendering();
        HRESULT hr = g_OriginalPresent1(pSwapChain1, SyncInterval, PresentFlags, pPresentParameters);
        FramePacer::Get().OnAfterPresent(pSwapChain1, hr);
        return hr;
    }

    HRESULT WINAPI DetourResizeBuffers(
        IDXGISwapChain* pSwapChain,
        UINT BufferCount,
        UINT Width,
        UINT Height,
        DXGI_FORMAT NewFormat,
        UINT SwapChainFlags)
    {
        HRESULT hr = g_OriginalResizeBuffers(pSwapChain, BufferCount, Width, Height, NewFormat, SwapChainFlags);
        if (SUCCEEDED(hr))
        {
            FramePacer::Get().EnforceSwapChainPolicies(pSwapChain);
        }
        return hr;
    }

    bool HookSwapChain(IDXGISwapChain* pSwapChain)
    {
        if (!pSwapChain) return false;

        std::lock_guard<std::mutex> lock(g_hookMutex);

        void** vtable = *reinterpret_cast<void***>(pSwapChain);
        if (!vtable) return false;

        if (g_hookedVtables.find(vtable) != g_hookedVtables.end())
        {
            return true;
        }

        // Tag swapchain with AMD Anti-Lag 2 private data if enabled
        const auto& config = AetherConfig::Get();
        if (config.pacing.enableAntiLag2)
        {
            AntiLag2Bridge::Get().TagSwapChain(pSwapChain, true, config.pacing.targetFpsCap);
        }

        // Present is slot 8 in IDXGISwapChain
        void* pPresentTarget = vtable[8];
        if (pPresentTarget && !g_OriginalPresent)
        {
            if (MH_CreateHook(pPresentTarget, reinterpret_cast<LPVOID>(&DetourPresent), reinterpret_cast<LPVOID*>(&g_OriginalPresent)) == MH_OK)
            {
                MH_EnableHook(pPresentTarget);
            }
        }

        // ResizeBuffers is slot 13 in IDXGISwapChain
        void* pResizeTarget = vtable[13];
        if (pResizeTarget && !g_OriginalResizeBuffers)
        {
            if (MH_CreateHook(pResizeTarget, reinterpret_cast<LPVOID>(&DetourResizeBuffers), reinterpret_cast<LPVOID*>(&g_OriginalResizeBuffers)) == MH_OK)
            {
                MH_EnableHook(pResizeTarget);
            }
        }

        // Check if IDXGISwapChain1 is implemented
        IDXGISwapChain1* pSwapChain1 = nullptr;
        if (SUCCEEDED(pSwapChain->QueryInterface(__uuidof(IDXGISwapChain1), reinterpret_cast<void**>(&pSwapChain1))) && pSwapChain1)
        {
            void** vtable1 = *reinterpret_cast<void***>(pSwapChain1);
            // Present1 is slot 22 in IDXGISwapChain1
            void* pPresent1Target = vtable1[22];
            if (pPresent1Target && !g_OriginalPresent1)
            {
                if (MH_CreateHook(pPresent1Target, reinterpret_cast<LPVOID>(&DetourPresent1), reinterpret_cast<LPVOID*>(&g_OriginalPresent1)) == MH_OK)
                {
                    MH_EnableHook(pPresent1Target);
                }
            }
            pSwapChain1->Release();
        }

        FramePacer::Get().EnforceSwapChainPolicies(pSwapChain);
        g_hookedVtables.insert(vtable);
        return true;
    }

    bool Initialize()
    {
        LoadSystemDXGI();
        MH_Initialize();
        FramePacer::Get().Initialize();

        // Create a dummy swapchain to immediately hook IDXGISwapChain VTable entries
        WNDCLASSEXW wc = { sizeof(WNDCLASSEXW), CS_CLASSDC, DefWindowProcW, 0L, 0L, GetModuleHandle(nullptr), nullptr, nullptr, nullptr, nullptr, L"AetherPulseDummy", nullptr };
        RegisterClassExW(&wc);
        HWND hWnd = CreateWindowW(L"AetherPulseDummy", L"", WS_OVERLAPPEDWINDOW, 0, 0, 100, 100, nullptr, nullptr, wc.hInstance, nullptr);

        if (hWnd)
        {
            D3D_FEATURE_LEVEL featureLevel;
            const D3D_FEATURE_LEVEL featureLevels[] = { D3D_FEATURE_LEVEL_11_0 };

            DXGI_SWAP_CHAIN_DESC sd = {};
            sd.BufferCount = 1;
            sd.BufferDesc.Width = 100;
            sd.BufferDesc.Height = 100;
            sd.BufferDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
            sd.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
            sd.OutputWindow = hWnd;
            sd.SampleDesc.Count = 1;
            sd.Windowed = TRUE;
            sd.SwapEffect = DXGI_SWAP_EFFECT_DISCARD;

            ID3D11Device* pDevice = nullptr;
            ID3D11DeviceContext* pContext = nullptr;
            IDXGISwapChain* pDummySwapChain = nullptr;

            HMODULE hD3D11 = LoadLibraryA("d3d11.dll");
            if (hD3D11)
            {
                typedef HRESULT(WINAPI* PFN_D3D11CreateDeviceAndSwapChain)(
                    IDXGIAdapter*, D3D_DRIVER_TYPE, HMODULE, UINT,
                    const D3D_FEATURE_LEVEL*, UINT, UINT,
                    const DXGI_SWAP_CHAIN_DESC*, IDXGISwapChain**,
                    ID3D11Device**, D3D_FEATURE_LEVEL*, ID3D11DeviceContext**);

                PFN_D3D11CreateDeviceAndSwapChain pD3D11CreateDeviceAndSwapChain =
                    reinterpret_cast<PFN_D3D11CreateDeviceAndSwapChain>(GetProcAddress(hD3D11, "D3D11CreateDeviceAndSwapChain"));

                if (pD3D11CreateDeviceAndSwapChain)
                {
                    if (SUCCEEDED(pD3D11CreateDeviceAndSwapChain(
                        nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0,
                        featureLevels, 1, D3D11_SDK_VERSION,
                        &sd, &pDummySwapChain, &pDevice, &featureLevel, &pContext)))
                    {
                        HookSwapChain(pDummySwapChain);
                        pDummySwapChain->Release();
                    }
                }
                if (pContext) pContext->Release();
                if (pDevice) pDevice->Release();
            }

            DestroyWindow(hWnd);
            UnregisterClassW(L"AetherPulseDummy", wc.hInstance);
        }

        return true;
    }

    void Shutdown()
    {
        MH_DisableHook(MH_ALL_HOOKS);
        MH_Uninitialize();
        FramePacer::Get().Shutdown();

        if (g_hRealDXGI)
        {
            FreeLibrary(g_hRealDXGI);
            g_hRealDXGI = nullptr;
        }
    }
}

extern "C" {
    HRESULT WINAPI ProxyCreateDXGIFactory(REFIID riid, void** ppFactory)
    {
        LoadSystemDXGI();
        if (!g_RealCreateDXGIFactory) return E_FAIL;
        return g_RealCreateDXGIFactory(riid, ppFactory);
    }

    HRESULT WINAPI ProxyCreateDXGIFactory1(REFIID riid, void** ppFactory)
    {
        LoadSystemDXGI();
        if (!g_RealCreateDXGIFactory1) return E_FAIL;
        return g_RealCreateDXGIFactory1(riid, ppFactory);
    }

    HRESULT WINAPI ProxyCreateDXGIFactory2(UINT Flags, REFIID riid, void** ppFactory)
    {
        LoadSystemDXGI();
        if (!g_RealCreateDXGIFactory2) return E_FAIL;
        return g_RealCreateDXGIFactory2(Flags, riid, ppFactory);
    }

    HRESULT WINAPI ProxyDXGID3D10CreateDevice(HMODULE hModule, IDXGIFactory* pFactory, IDXGIAdapter* pAdapter, UINT Flags, void* pUnknown, void** ppDevice)
    {
        LoadSystemDXGI();
        if (!g_RealDXGID3D10CreateDevice) return E_FAIL;
        return g_RealDXGID3D10CreateDevice(hModule, pFactory, pAdapter, Flags, pUnknown, ppDevice);
    }

    HRESULT WINAPI ProxyDXGID3D10RegisterLayers(const void* pLayers, UINT NumLayers)
    {
        LoadSystemDXGI();
        if (!g_RealDXGID3D10RegisterLayers) return E_FAIL;
        return g_RealDXGID3D10RegisterLayers(pLayers, NumLayers);
    }

    HRESULT WINAPI ProxyDXGIGetDebugInterface1(UINT Flags, REFIID riid, void** ppDebug)
    {
        LoadSystemDXGI();
        if (!g_RealDXGIGetDebugInterface1) return E_FAIL;
        return g_RealDXGIGetDebugInterface1(Flags, riid, ppDebug);
    }

    HRESULT WINAPI ProxyDXGIDumpLiveObjects(void)
    {
        LoadSystemDXGI();
        if (!g_RealDXGIDumpLiveObjects) return E_FAIL;
        return g_RealDXGIDumpLiveObjects();
    }
}
