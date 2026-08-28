#pragma once

#include <windows.h>
#include <dxgi.h>
#include <dxgi1_6.h>
#include <atomic>
#include <cstdint>
#include "IFSRFramePacer.h"

class FramePacer : public IFSRFramePacer
{
public:
    static FramePacer& Get();

    bool Initialize() override;
    void Shutdown() override;

    // Invoked before and after Present / Present1
    void OnBeforePresent(IDXGISwapChain* pSwapChain, UINT SyncInterval, UINT Flags) override;
    void OnAfterPresent(IDXGISwapChain* pSwapChain, HRESULT presentResult) override;

    // Apply SwapChain enhancements (Flip discard & Max latency = 1)
    void EnforceSwapChainPolicies(IDXGISwapChain* pSwapChain) override;

    // Pacing state query
    bool IsActive() const override { return m_initialized.load(); }
    float GetCurrentFps() const override { return m_currentFps.load(); }
    float GetFrameTimeMs() const override { return m_currentFrameTimeMs.load(); }
    float GetPacingJitterMs() const override { return m_currentJitterMs.load(); }
    uint32_t GetMissedDeadlines() const override { return m_missedDeadlines.load(); }

private:
    FramePacer();
    ~FramePacer();

    FramePacer(const FramePacer&) = delete;
    FramePacer& operator=(const FramePacer&) = delete;

    void PreciseDelayUntil(int64_t targetQpcTicks);

    std::atomic<bool> m_initialized{ false };
    HANDLE            m_hWaitableTimer = nullptr;

    LARGE_INTEGER     m_qpcFrequency{ 0 };
    int64_t           m_lastPresentQpc = 0;
    int64_t           m_lastPostPresentQpc = 0;
    int64_t           m_targetNextPresentQpc = 0;

    // Rolling Exponential Moving Average (16-frame window)
    double            m_emaFrameTicks = 0.0;
    bool              m_firstFrame = true;
    uint32_t          m_frameIndex = 0;
    uint32_t          m_lastObservedFpsCap = 0;

    // Telemetry and statistics
    std::atomic<float>    m_currentFps{ 0.0f };
    std::atomic<float>    m_currentFrameTimeMs{ 0.0f };
    std::atomic<float>    m_currentJitterMs{ 0.0f };
    std::atomic<uint32_t> m_missedDeadlines{ 0 };

    int64_t           m_lastTelemetryQpc = 0;
    uint32_t          m_telemetryFrameCounter = 0;
};