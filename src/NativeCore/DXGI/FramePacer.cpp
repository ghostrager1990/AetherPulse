#include "FramePacer.h"
#include "../Shared/AetherTelemetry.h"
#include "../Shared/Config.h"
#include <cmath>
#include <algorithm>
#include <d3d11.h>
#include <d3d12.h>

#ifndef CREATE_WAITABLE_TIMER_HIGH_RESOLUTION
#define CREATE_WAITABLE_TIMER_HIGH_RESOLUTION 0x00000002
#endif

FramePacer::FramePacer()
{
    QueryPerformanceFrequency(&m_qpcFrequency);
}

FramePacer::~FramePacer()
{
    Shutdown();
}

FramePacer& FramePacer::Get()
{
    static FramePacer instance;
    return instance;
}

bool FramePacer::Initialize()
{
    if (m_initialized.load()) return true;

    QueryPerformanceFrequency(&m_qpcFrequency);

    // Create high-resolution waitable timer
    m_hWaitableTimer = CreateWaitableTimerExW(
        nullptr,
        nullptr,
        CREATE_WAITABLE_TIMER_HIGH_RESOLUTION,
        TIMER_ALL_ACCESS
    );

    if (!m_hWaitableTimer)
    {
        // Fallback to standard manual-reset timer if high-resolution flag is unsupported on legacy OS
        m_hWaitableTimer = CreateWaitableTimerExW(nullptr, nullptr, 0, TIMER_ALL_ACCESS);
    }

    m_firstFrame = true;
    m_emaFrameTicks = 0.0;
    m_lastPresentQpc = 0;
    m_targetNextPresentQpc = 0;
    m_frameIndex = 0;
    m_telemetryFrameCounter = 0;

    LARGE_INTEGER now;
    QueryPerformanceCounter(&now);
    m_lastTelemetryQpc = now.QuadPart;

    m_initialized.store(m_hWaitableTimer != nullptr);
    return m_initialized.load();
}

void FramePacer::Shutdown()
{
    m_initialized.store(false);
    if (m_hWaitableTimer)
    {
        CloseHandle(m_hWaitableTimer);
        m_hWaitableTimer = nullptr;
    }
}

void FramePacer::EnforceSwapChainPolicies(IDXGISwapChain* pSwapChain)
{
    if (!pSwapChain) return;

    const auto& config = AetherConfig::Get();

    // Query DXGI Device to set maximum frame latency = 1
    IDXGIDevice1* pDXGIDevice = nullptr;
    if (SUCCEEDED(pSwapChain->GetDevice(__uuidof(IDXGIDevice1), reinterpret_cast<void**>(&pDXGIDevice))) && pDXGIDevice)
    {
        pDXGIDevice->SetMaximumFrameLatency(config.pacing.maxFrameLatency);
        pDXGIDevice->Release();
    }

    // Check for IDXGISwapChain2 to set frame latency waitable object
    IDXGISwapChain2* pSwapChain2 = nullptr;
    if (SUCCEEDED(pSwapChain->QueryInterface(__uuidof(IDXGISwapChain2), reinterpret_cast<void**>(&pSwapChain2))) && pSwapChain2)
    {
        pSwapChain2->SetMaximumFrameLatency(config.pacing.maxFrameLatency);
        pSwapChain2->Release();
    }
}

void FramePacer::PreciseDelayUntil(int64_t targetQpcTicks)
{
    if (!m_hWaitableTimer) return;

    LARGE_INTEGER currentQpc;
    QueryPerformanceCounter(&currentQpc);

    int64_t ticksRemaining = targetQpcTicks - currentQpc.QuadPart;
    if (ticksRemaining <= 0)
    {
        return; // Deadline already reached
    }

    const auto& config = AetherConfig::Get();
    double ticksPerMicrosecond = static_cast<double>(m_qpcFrequency.QuadPart) / 1000000.0;
    int64_t spinThresholdTicks = static_cast<int64_t>(config.pacing.spinYieldMicroseconds * ticksPerMicrosecond);

    // Sleep phase using High-Resolution Waitable Timer
    if (ticksRemaining > spinThresholdTicks)
    {
        int64_t sleepTicks = ticksRemaining - spinThresholdTicks;
        // Convert QPC ticks to 100-nanosecond intervals (negative for relative time in SetWaitableTimer)
        // 100ns units = (sleepTicks * 10,000,000) / qpcFrequency
        int64_t sleepUnits100Ns = -((sleepTicks * 10000000LL) / m_qpcFrequency.QuadPart);

        if (sleepUnits100Ns < 0)
        {
            LARGE_INTEGER dueTime;
            dueTime.QuadPart = sleepUnits100Ns;
            if (SetWaitableTimer(m_hWaitableTimer, &dueTime, 0, nullptr, nullptr, FALSE))
            {
                WaitForSingleObject(m_hWaitableTimer, INFINITE);
            }
        }
    }

    // Precision spin-wait phase to eliminate timer wake jitter
    QueryPerformanceCounter(&currentQpc);
    while (currentQpc.QuadPart < targetQpcTicks)
    {
        int64_t remaining = targetQpcTicks - currentQpc.QuadPart;
        if (remaining > static_cast<int64_t>(ticksPerMicrosecond * 50.0))
        {
            SwitchToThread();
        }
        else
        {
            YieldProcessor();
        }
        QueryPerformanceCounter(&currentQpc);
    }
}

void FramePacer::OnBeforePresent(IDXGISwapChain* pSwapChain, UINT SyncInterval, UINT Flags)
{
    if (!m_initialized.load())
    {
        Initialize();
    }

    const auto& config = AetherConfig::Get();
    if (!config.pacing.enablePacing)
    {
        return;
    }

    LARGE_INTEGER now;
    QueryPerformanceCounter(&now);
    int64_t currentQpc = now.QuadPart;

    if (m_firstFrame)
    {
        m_lastPresentQpc = currentQpc;
        m_firstFrame = false;
        return;
    }

    int64_t elapsedTicks = currentQpc - m_lastPresentQpc;
    if (elapsedTicks <= 0) elapsedTicks = 1;

    // Update 16-frame rolling Exponential Moving Average (EMA)
    float alpha = config.pacing.emaAlpha;
    if (m_emaFrameTicks <= 0.0)
    {
        m_emaFrameTicks = static_cast<double>(elapsedTicks);
    }
    else
    {
        m_emaFrameTicks = (alpha * static_cast<double>(elapsedTicks)) + ((1.0 - alpha) * m_emaFrameTicks);
    }

    // Determine Multi-Frame Generation Multiplier (1x to 6x, or Adaptive)
    int multiplier = 1;
    if (config.pacing.enablePacing || config.pacing.enableHalfIntervalPacing)
    {
        if (config.pacing.multiplierMode == FrameGenMultiplier::Adaptive)
        {
            float currentNativeFps = m_currentFps.load(std::memory_order_relaxed);
            if (currentNativeFps <= 0.0f && m_emaFrameTicks > 0.0)
            {
                currentNativeFps = static_cast<float>(static_cast<double>(m_qpcFrequency.QuadPart) / m_emaFrameTicks);
            }
            if (currentNativeFps <= 1.0f) currentNativeFps = 60.0f;

            // int multiplier = std::clamp((int)std::ceil((float)targetFps / currentNativeFps), 1, 6);
            multiplier = std::clamp(static_cast<int>(std::ceil(static_cast<float>(config.pacing.targetFps) / currentNativeFps)), 1, 6);
        }
        else
        {
            multiplier = std::clamp(static_cast<int>(config.pacing.multiplierMode), 1, 6);
        }
    }

    // Determine target cadence interval scaled by multiplier: EMA / multiplier
    double targetIntervalTicks = m_emaFrameTicks / static_cast<double>(multiplier);

    if (config.pacing.targetFpsCap > 0)
    {
        // Enforce hard frame rate cap
        double capTicks = static_cast<double>(m_qpcFrequency.QuadPart) / static_cast<double>(config.pacing.targetFpsCap);
        targetIntervalTicks = (std::max)(targetIntervalTicks, capTicks);
    }

    int64_t targetPresentTime = m_lastPresentQpc + static_cast<int64_t>(targetIntervalTicks);

    // Meter presentation timing via high-precision timer
    if (targetPresentTime > currentQpc)
    {
        PreciseDelayUntil(targetPresentTime);
    }
    else if ((currentQpc - targetPresentTime) > static_cast<int64_t>(m_qpcFrequency.QuadPart / 60))
    {
        // Missed deadline by more than 16ms
        m_missedDeadlines.fetch_add(1, std::memory_order_relaxed);
    }

    // Calculate jitter
    QueryPerformanceCounter(&now);
    int64_t actualPresentQpc = now.QuadPart;
    double jitterTicks = std::abs(static_cast<double>(actualPresentQpc - targetPresentTime));
    float jitterMs = static_cast<float>((jitterTicks * 1000.0) / static_cast<double>(m_qpcFrequency.QuadPart));
    m_currentJitterMs.store(jitterMs, std::memory_order_relaxed);
}

void FramePacer::OnAfterPresent(IDXGISwapChain* pSwapChain, HRESULT presentResult)
{
    LARGE_INTEGER now;
    QueryPerformanceCounter(&now);
    int64_t currentQpc = now.QuadPart;

    int64_t frameDeltaTicks = currentQpc - m_lastPresentQpc;
    if (frameDeltaTicks <= 0) frameDeltaTicks = 1;

    m_lastPresentQpc = currentQpc;
    m_frameIndex++;
    m_telemetryFrameCounter++;

    float frameTimeMs = static_cast<float>((static_cast<double>(frameDeltaTicks) * 1000.0) / static_cast<double>(m_qpcFrequency.QuadPart));
    m_currentFrameTimeMs.store(frameTimeMs, std::memory_order_relaxed);

    // Update FPS calculation periodically (every 100ms or 16 frames)
    int64_t telemetryElapsed = currentQpc - m_lastTelemetryQpc;
    double telemetryElapsedMs = (static_cast<double>(telemetryElapsed) * 1000.0) / static_cast<double>(m_qpcFrequency.QuadPart);

    if (telemetryElapsedMs >= 100.0 && m_telemetryFrameCounter > 0)
    {
        float fps = static_cast<float>((m_telemetryFrameCounter * 1000.0) / telemetryElapsedMs);
        m_currentFps.store(fps, std::memory_order_relaxed);

        m_lastTelemetryQpc = currentQpc;
        m_telemetryFrameCounter = 0;

        // Push telemetry update to shared memory buffer
        const auto& config = AetherConfig::Get();
        if (config.telemetry.enableSharedMemory)
        {
            AetherTelemetryServer::Get().UpdateTelemetry(
                m_currentFps.load(std::memory_order_relaxed),
                m_currentFrameTimeMs.load(std::memory_order_relaxed),
                m_currentJitterMs.load(std::memory_order_relaxed),
                config.pacing.enablePacing,
                config.denoiser.enableRayRegen,
                0x1 /* Ray Reconstruction active */,
                m_missedDeadlines.load(std::memory_order_relaxed)
            );
        }
    }
}
