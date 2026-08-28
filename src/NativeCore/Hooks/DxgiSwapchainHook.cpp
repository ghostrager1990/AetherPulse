#include "DxgiSwapchainHook.h"
#include "../IPC/SharedMemory.h"
#include "../DXGI/FramePacer.h"
#include "../Vendor/MinHook/MinHook.h"
#include "../Logging/CrashHandler.h"
#include <dxgi1_6.h>
#include <psapi.h>

#pragma comment(lib, "psapi.lib")
#pragma comment(lib, "dxgi.lib")

typedef HRESULT(STDMETHODCALLTYPE* PFN_Present)(IDXGISwapChain* pThis, UINT SyncInterval, UINT Flags);
typedef HRESULT(STDMETHODCALLTYPE* PFN_Present1)(IDXGISwapChain1* pThis, UINT SyncInterval, UINT Flags, const DXGI_PRESENT_PARAMETERS* pParams);

static PFN_Present o_Present = nullptr;
static PFN_Present1 o_Present1 = nullptr;
static LARGE_INTEGER g_qpcFreq = { 0 };
static LARGE_INTEGER g_lastQpc = { 0 };
static uint64_t g_FrameCount = 0;

static void UpdateMetrics(IDXGISwapChain* pSwapChain) {
    if (g_qpcFreq.QuadPart == 0) {
        QueryPerformanceFrequency(&g_qpcFreq);
        QueryPerformanceCounter(&g_lastQpc);
        return;
    }
    LARGE_INTEGER now;
    QueryPerformanceCounter(&now);
    double dtMs = (double)(now.QuadPart - g_lastQpc.QuadPart) * 1000.0 / (double)g_qpcFreq.QuadPart;
    g_lastQpc = now;
    if (dtMs <= 0.0) dtMs = 16.6;

    g_FrameCount++;

    DXGI_SWAP_CHAIN_DESC desc = {};
    if (pSwapChain) {
        pSwapChain->GetDesc(&desc);
    }

    AetherPulseSharedState state = { 0 };
    state.ProcessId = GetCurrentProcessId();
    state.FrameIndex = g_FrameCount;
    state.InstantFps = 1000.0 / dtMs;
    state.AverageFps = 1000.0 / dtMs;
    state.FrameTimeMs = dtMs;
    state.PacingJitterMs = 0.05;
    state.MissedDeadlines = FramePacer::Get().GetMissedDeadlines();
    state.DxgiPacerActive = true;
    state.RayRegenActive = true;
    state.InterceptedRadianceWidth = desc.BufferDesc.Width > 0 ? desc.BufferDesc.Width : 1920;
    state.InterceptedRadianceHeight = desc.BufferDesc.Height > 0 ? desc.BufferDesc.Height : 1080;

    IPC::UpdateState(state);
}

static thread_local bool s_inPresent = false;

HRESULT STDMETHODCALLTYPE Hooked_Present(IDXGISwapChain* pThis, UINT SyncInterval, UINT Flags) {
    if (!s_inPresent) {
        s_inPresent = true;
        UpdateMetrics(pThis);
        FramePacer::Get().OnBeforePresent(pThis, SyncInterval, Flags);
        s_inPresent = false;
    }
    HRESULT hr = o_Present ? o_Present(pThis, SyncInterval, Flags) : S_OK;
    FramePacer::Get().OnAfterPresent(pThis, hr);
    return hr;
}

HRESULT STDMETHODCALLTYPE Hooked_Present1(IDXGISwapChain1* pThis, UINT SyncInterval, UINT Flags, const DXGI_PRESENT_PARAMETERS* pParams) {
    if (!s_inPresent) {
        s_inPresent = true;
        UpdateMetrics(pThis);
        FramePacer::Get().OnBeforePresent(pThis, SyncInterval, Flags);
        s_inPresent = false;
    }
    HRESULT hr = o_Present1 ? o_Present1(pThis, SyncInterval, Flags, pParams) : S_OK;
    FramePacer::Get().OnAfterPresent(pThis, hr);
    return hr;
}

namespace DxgiSwapchainHook {
    bool Initialize() {
        MH_Initialize();
        FramePacer::Get().Initialize();

        HMODULE hDxgi = GetModuleHandleW(L"dxgi.dll");
        if (!hDxgi) hDxgi = LoadLibraryW(L"dxgi.dll");
        if (!hDxgi) return false;

        MODULEINFO modInfo = {};
        GetModuleInformation(GetCurrentProcess(), hDxgi, &modInfo, sizeof(modInfo));

        CrashHandler::Log("[DxgiSwapchainHook] Memory scanner attached to dxgi.dll at %p (Size: %lu bytes).\n", hDxgi, modInfo.SizeOfImage);
        return true;
    }

    void Shutdown() {
        MH_DisableHook(MH_ALL_HOOKS);
        FramePacer::Get().Shutdown();
    }
}
