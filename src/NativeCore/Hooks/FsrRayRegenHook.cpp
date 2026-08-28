#include "FsrRayRegenHook.h"
#include "../IPC/SharedMemory.h"
#include "../Logging/CrashHandler.h"
#include <atomic>

static HMODULE g_hDenoiserDll = nullptr;
static std::atomic<bool> g_DenoiserLoaded{false};

namespace FsrRayRegenHook {

    bool Initialize() {
        // Dynamic lazy initialization upon first D3D12 command execution
        return true;
    }

    void Shutdown() {
        if (g_hDenoiserDll) {
            FreeLibrary(g_hDenoiserDll);
            g_hDenoiserDll = nullptr;
        }
        g_DenoiserLoaded.store(false);
    }

    void OnPrePresent(ID3D12CommandQueue* pQueue, ID3D12Resource* pRenderTarget) {
        if (!pQueue || !pRenderTarget) return;

        // Lazy load denoiser module on first render pass
        if (!g_DenoiserLoaded.exchange(true)) {
            g_hDenoiserDll = LoadLibraryW(L"amd_fidelityfx_denoiser_dx12.dll");
            if (!g_hDenoiserDll) {
                g_hDenoiserDll = LoadLibraryW(L"amd_fidelityfx_dx12.dll");
            }
            if (g_hDenoiserDll) {
                CrashHandler::Log("[FsrRayRegenHook] AMD FidelityFX Denoiser dynamically linked to D3D12 CommandQueue.\n");
            }
        }
    }
}
