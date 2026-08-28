#include "FramePacer.h"
#include "../Shared/Config.h"
#include <cmath>
#include <algorithm>
#include <d3d11.h>
#include <d3d12.h>

#ifndef CREATE_WAITABLE_TIMER_HIGH_RESOLUTION
#define CREATE_WAITABLE_TIMER_HIGH_RESOLUTION 0x00000002
#endif

FramePacer::FramePacer() {
    QueryPerformanceFrequency(&m_qpcFrequency);
}

FramePacer::~FramePacer() {
    Shutdown();
}

FramePacer& FramePacer::Get() {
    static FramePacer instance;
    return instance;
}

bool FramePacer::Initialize() {
    if (m_initialized.load()) return true;
    QueryPerformanceFrequency(&m_qpcFrequency);
    m_hWaitableTimer = CreateWaitableTimerExW(nullptr, nullptr, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
    if (!m_hWaitableTimer) {
        m_hWaitableTimer = CreateWaitableTimerExW(nullptr, nullptr, 0, TIMER_ALL_ACCESS);
    }
    m_firstFrame = true;
    m_emaFrameTicks = 0.0;
    m_lastPresentQpc = 0;
    m_lastPostPresentQpc = 0;
    m_targetNextPresentQpc = 0;
    m_frameIndex = 0;
    m_telemetryFrameCounter = 0;
    m_lastObservedFpsCap = 0;

    LARGE_INTEGER now;
    QueryPerformanceCounter(&now);
    m_lastTelemetryQpc = now.QuadPart;
    m_initialized.store(m_hWaitableTimer != nullptr);
    return m_initialized.load();
}

void FramePacer::Shutdown() {
    m_initialized.store(false);
    if (m_hWaitableTimer) {
        CloseHandle(m_hWaitableTimer);
        m_hWaitableTimer = nullptr;
    }
}

void FramePacer::EnforceSwapChainPolicies(IDXGISwapChain* pSwapChain) {
    if (!pSwapChain) return;
    const auto& config = AetherConfig::Get();
    
    IDXGIDevice1* pDXGIDevice = nullptr;
    if (SUCCEEDED(pSwapChain->GetDevice(__uuidof(IDXGIDevice1), reinterpret_cast<void**>(&pDXGIDevice))) && pDXGIDevice) {
        pDXGIDevice->SetMaximumFrameLatency(config.pacing.maxFrameLatency);
        pDXGIDevice->Release();
    }
    IDXGISwapChain2* pSwapChain2 = nullptr;
    if (SUCCEEDED(pSwapChain->QueryInterface(__uuidof(IDXGISwapChain2), reinterpret_cast<void**>(&pSwapChain2))) && pSwapChain2) {
        pSwapChain2->SetMaximumFrameLatency(config.pacing.maxFrameLatency);
        pSwapChain2->Release();
    }
}

void FramePacer::PreciseDelayUntil(int64_t targetQpcTicks) {
    if (!m_hWaitableTimer) return;
    LARGE_INTEGER currentQpc;
    QueryPerformanceCounter(&currentQpc);
    int64_t ticksRemaining = targetQpcTicks - currentQpc.QuadPart;
    if (ticksRemaining <= 0) return;

    const auto& config = AetherConfig::Get();
    double ticksPerMicrosecond = static_cast<double>(m_qpcFrequency.QuadPart) / 1000000.0;
    int64_t spinThresholdTicks = static_cast<int64_t>(config.pacing.spinYieldMicroseconds * ticksPerMicrosecond);

    if (ticksRemaining > spinThresholdTicks) {
        int64_t sleepTicks = ticksRemaining - spinThresholdTicks;
        int64_t sleepUnits100Ns = -((sleepTicks * 10000000LL) / m_qpcFrequency.QuadPart);
        if (sleepUnits100Ns < 0) {
            LARGE_INTEGER dueTime;
            dueTime.QuadPart = sleepUnits100Ns;
            if (SetWaitableTimer(m_hWaitableTimer, &dueTime, 0, nullptr, nullptr, FALSE)) {
                WaitForSingleObject(m_hWaitableTimer, INFINITE);
            }
        }
    }

    QueryPerformanceCounter(&currentQpc);
    while (currentQpc.QuadPart < targetQpcTicks) {
        int64_t remaining = targetQpcTicks - currentQpc.QuadPart;
        if (remaining > static_cast<int64_t>(ticksPerMicrosecond * 50.0)) {
            SwitchToThread();
        } else {
            YieldProcessor();
        }
        QueryPerformanceCounter(&currentQpc);
    }
}

void FramePacer::OnBeforePresent(IDXGISwapChain* pSwapChain, UINT SyncInterval, UINT Flags) {
    if (!m_initialized.load()) Initialize();

    const auto& config = AetherConfig::Get();
    if (!config.pacing.enablePacing) return;

    LARGE_INTEGER now;
    QueryPerformanceCounter(&now);
    int64_t currentQpc = now.QuadPart;

    if (config.pacing.targetFpsCap != m_lastObservedFpsCap) {
        m_lastObservedFpsCap = config.pacing.targetFpsCap;
        m_emaFrameTicks = 0.0;
        m_lastPresentQpc = currentQpc;
        m_lastPostPresentQpc = currentQpc;
        EnforceSwapChainPolicies(pSwapChain);
        return;
    }

    if (m_firstFrame) {
        m_lastPresentQpc = currentQpc;
        m_lastPostPresentQpc = currentQpc;
        m_firstFrame = false;
        return;
    }

    // Measure raw delta for telemetry calculation
    int64_t rawDelta = currentQpc - m_lastPostPresentQpc;
    if (rawDelta <= 0) rawDelta = 1;
    float alpha = config.pacing.emaAlpha > 0.0f ? config.pacing.emaAlpha : 0.125f;
    if (m_emaFrameTicks <= 0.0) {
        m_emaFrameTicks = static_cast<double>(rawDelta);
    } else {
        m_emaFrameTicks = (alpha * static_cast<double>(rawDelta)) + ((1.0 - alpha) * m_emaFrameTicks);
    }

    // AUTO / UNCAPPED MODE: DO NOT DELAY PRESENT CALLS
    if (config.pacing.targetFpsCap <= 0) {
        m_lastPresentQpc = currentQpc;
        m_currentJitterMs.store(0.0f, std::memory_order_relaxed);
        return;
    }

    // Explicit FPS Cap Pacing
    double capTicks = static_cast<double>(m_qpcFrequency.QuadPart) / static_cast<double>(config.pacing.targetFpsCap);
    int64_t targetPresentTime = m_lastPresentQpc + static_cast<int64_t>(capTicks);

    if ((targetPresentTime - currentQpc) > static_cast<int64_t>(capTicks)) {
        targetPresentTime = currentQpc + static_cast<int64_t>(capTicks);
    }

    if (targetPresentTime > currentQpc) {
        PreciseDelayUntil(targetPresentTime);
    }

    QueryPerformanceCounter(&now);
    int64_t actualPresentQpc = now.QuadPart;
    double jitterTicks = std::abs(static_cast<double>(actualPresentQpc - targetPresentTime));
    float jitterMs = static_cast<float>((jitterTicks * 1000.0) / static_cast<double>(m_qpcFrequency.QuadPart));
    m_currentJitterMs.store(jitterMs, std::memory_order_relaxed);
}

void FramePacer::OnAfterPresent(IDXGISwapChain* pSwapChain, HRESULT presentResult) {
    LARGE_INTEGER now;
    QueryPerformanceCounter(&now);
    int64_t currentQpc = now.QuadPart;
    int64_t frameDeltaTicks = currentQpc - m_lastPresentQpc;
    if (frameDeltaTicks <= 0) frameDeltaTicks = 1;

    m_lastPresentQpc = currentQpc;
    m_lastPostPresentQpc = currentQpc;
    m_frameIndex++;
    m_telemetryFrameCounter++;

    float frameTimeMs = static_cast<float>((static_cast<double>(frameDeltaTicks) * 1000.0) / static_cast<double>(m_qpcFrequency.QuadPart));
    m_currentFrameTimeMs.store(frameTimeMs, std::memory_order_relaxed);

    int64_t telemetryElapsed = currentQpc - m_lastTelemetryQpc;
    double telemetryElapsedMs = (static_cast<double>(telemetryElapsed) * 1000.0) / static_cast<double>(m_qpcFrequency.QuadPart);

    if (telemetryElapsedMs >= 100.0 && m_telemetryFrameCounter > 0) {
        float fps = static_cast<float>((m_telemetryFrameCounter * 1000.0) / telemetryElapsedMs);
        m_currentFps.store(fps, std::memory_order_relaxed);
        m_lastTelemetryQpc = currentQpc;
        m_telemetryFrameCounter = 0;
    }
}