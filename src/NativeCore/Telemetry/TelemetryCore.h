#pragma once

#include <windows.h>
#include <cstdint>
#include <atomic>
#include "../Shared/RTSSSharedMemory.h"

#pragma pack(push, 1)
struct AetherTelemetryData
{
    uint32_t Sequence;
    uint32_t StructVersion;
    uint32_t FrameIndex;
    float    CurrentFps;
    float    AverageFps;
    float    FrameTimeMs;
    float    PacingJitterMs;
    uint32_t DroppedFrames;
    uint8_t  IsPacerActive;
    uint8_t  IsRayRegenActive;
    uint32_t ActiveDenoiserFlags;
    float    CadenceRatio;               // Real vs Interpolated distribution (0.50f = 50:50)
    float    SubFrameVarianceUs;         // Sub-frame interval variance (µs)
    float    RealTimeDeltaMs;            // Real-time presentation delta (ms)
    uint8_t  IsExternalLimiterActive;    // 1 = RTSS / External Limiter detected & passthrough active
    char     RawGameTitle[128];
};
#pragma pack(pop)

class TelemetryCore
{
public:
    static TelemetryCore& Get();

    void Initialize();
    void Shutdown();
    void RecordPresent();
    void RecordPresent(bool isPacerActive, bool isRayRegenActive, uint32_t denoiserFlags);
    void UpdateLiveMetrics(float currentFps, float frameTimeMs, bool isPacerActive = true, bool isRayRegenActive = false, uint32_t denoiserFlags = 0);

    bool IsActive() const { return m_pData != nullptr || m_pRtssAppEntry != nullptr; }

    uint32_t GetFramerateLimit() const;

private:
    TelemetryCore();
    ~TelemetryCore();

    TelemetryCore(const TelemetryCore&) = delete;
    TelemetryCore& operator=(const TelemetryCore&) = delete;

    HANDLE m_hMapFile = nullptr;
    AetherTelemetryData* m_pData = nullptr;

    // RTSS Standard Shared Memory mapping
    HANDLE m_hRtssMapFile = nullptr;
    RTSS_SHARED_MEMORY* m_pRtssHeader = nullptr;
    RTSS_SHARED_MEMORY_APP_ENTRY* m_pRtssAppEntry = nullptr;

    LARGE_INTEGER m_qpcFrequency = { 0 };
    LARGE_INTEGER m_lastPresentQpc = { 0 };
    double m_lastDeltaMs = 0.0;
    float m_runningAvgFps = 0.0f;
    uint32_t m_frameCounter = 0;
    uint32_t m_droppedFrames = 0;

    static const size_t HISTORY_CAPACITY = 120;
    float m_frameTimeHistory[HISTORY_CAPACITY] = { 0.0f };
    size_t m_historyIndex = 0;
    size_t m_historyCount = 0;

    float ComputeOnePercentLowFps() const;
};
