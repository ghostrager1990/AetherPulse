#pragma once
#include <windows.h>
#include <dxgi.h>
#include <dxgi1_6.h>
#include <cstdint>
#include <atomic>
#include "Shared/AetherPulseShared.h"

class FSRFramePacer {
private:
    HANDLE m_hWaitableTimer = NULL;
    HANDLE m_hMapFile = NULL;
    AetherPulsePacingIPC* m_pSharedMem = nullptr;
    LARGE_INTEGER m_qpcFreq = { 0 };
    LARGE_INTEGER m_lastPresentQpc = { 0 };
    LARGE_INTEGER m_lastRealFrameQpc = { 0 };
    int64_t m_smoothedBaseIntervalQpc = 0;
    bool m_isInterpolatedFrame = false;
    double m_lastDeltaMs = 0.0;
    std::atomic<bool> m_initialized{ false };
    std::atomic<float> m_currentFps{ 0.0f };
    std::atomic<float> m_frameTimeMs{ 0.0f };
    std::atomic<float> m_cadenceRatio{ 0.5f };
    std::atomic<float> m_subFrameVarianceUs{ 0.0f };
    std::atomic<bool> m_externalLimiterActive{ false };
    uint32_t m_tightClampStreak{ 0 };

public:
    static FSRFramePacer& GetInstance();
    bool Initialize();
    void Shutdown();
    void OnBeforePresent(IDXGISwapChain* pSwapChain, UINT SyncInterval, UINT Flags);
    void OnAfterPresent(IDXGISwapChain* pSwapChain, HRESULT presentResult);
    bool IsActive() const { return m_initialized && m_pSharedMem && m_pSharedMem->IsHookActive; }
    float GetCurrentFps() const { return m_currentFps.load(); }
    float GetFrameTimeMs() const { return m_frameTimeMs.load(); }
};

extern "C" __declspec(dllexport) FSRFramePacer* GetAetherPulsePacer();
