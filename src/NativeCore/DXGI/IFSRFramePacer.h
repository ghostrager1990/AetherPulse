#pragma once

#include <windows.h>
#include <dxgi.h>
#include <dxgi1_6.h>
#include <cstdint>

class IFSRFramePacer
{
public:
    virtual ~IFSRFramePacer() = default;

    virtual bool Initialize() = 0;
    virtual void Shutdown() = 0;

    virtual void OnBeforePresent(IDXGISwapChain* pSwapChain, UINT SyncInterval, UINT Flags) = 0;
    virtual void OnAfterPresent(IDXGISwapChain* pSwapChain, HRESULT presentResult) = 0;

    virtual void EnforceSwapChainPolicies(IDXGISwapChain* pSwapChain) = 0;

    virtual bool IsActive() const = 0;
    virtual float GetCurrentFps() const = 0;
    virtual float GetFrameTimeMs() const = 0;
    virtual float GetPacingJitterMs() const = 0;
    virtual uint32_t GetMissedDeadlines() const = 0;
};
