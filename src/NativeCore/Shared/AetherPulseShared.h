#pragma once
#include <cstdint>
#include <windows.h>

#pragma pack(push, 1)

struct AetherPulsePacingIPC {
    uint32_t TargetFps;          // 0 = Uncapped, 15..500
    uint32_t MultiplierMode;     // 2 = 2X, 3 = 3X, 4 = 4X
    float LatencyToleranceMs;    // e.g., 0.5 ms
    float SpinWaitThresholdMs;   // e.g., 4.0 ms
    float MaxDriftMs;            // e.g., 2.0 ms
    uint8_t EnablePacing;        // 1 = Active, 0 = Bypass
    uint8_t IsHookActive;        // Handshake flag written by Native Proxy (1 = Active)
    uint8_t AutoEma;             // 1 = Auto adaptive alpha based on frametime, 0 = Manual
    float ManualEmaAlpha;        // Manual EMA alpha (0.01 - 0.30)
    uint8_t IsExternalLimiterActive; // 1 = RTSS/External Limiter detected & auto-passthrough active
};

// Backward-compatible alias
typedef AetherPulsePacingIPC FSRPacingIPCData;

struct FSRSharedMemory {
    uint32_t RenderScalePreset; // 0=Native (1.0x), 1=Ultra Quality (1.3x), 2=Quality (1.5x), 3=Balanced (1.7x), 4=Perf (2.0x), 5=Ultra Perf (3.0x)
    uint32_t EnableRCAS;        // 0=Off, 1=On
    float RCASSharpness;        // 0.0f - 1.0f
    uint32_t AutoLodBias;       // 0=Off, 1=On
    float ManualLodBias;        // -2.0f to 0.0f
    uint32_t EnableReactiveMask;// 0=Off, 1=On
    uint32_t ClampDRS;          // 0=Off, 1=On
    float DRSFloorPercent;      // 0.50f - 1.0f
    uint32_t DebugPiPMode;      // 0=Off, 1=Raw vs RCAS Inset, 2=Reactive Mask Heatmap
};

struct RayRegenIPCData {
    uint32_t EnableNRC;
    uint32_t EnableDenoiser;
    float RoughnessThreshold;
    uint32_t SpatialFilterPasses;
    float TemporalWeight;
    float DepthSigma;
    float NormalSigma;
    uint32_t PerceptualColorCorrection;
    uint32_t EnableDisocclusionFilter;
};

struct TelemetrySharedMemory {
    uint32_t Signature; // 0x4150544D ("APTM")
    uint32_t ProcessId;
    float CurrentFPS;
    float AverageFPS;
    float FrametimeMs;
    float Frametime1PercentLowMs;
    float SwapchainPacingVarianceMs;
    uint64_t TotalPresentedFrames;
    uint32_t PresentFlags;
    uint32_t SyncInterval;
    float CadenceRatio;               // Real vs Interpolated distribution (0.50f = 50:50)
    float SubFrameIntervalVarianceUs; // Microsecond jitter (µs)
    float RealTimeDeltaMs;            // Real-time presentation delta (ms)
    uint8_t IsExternalLimiterActive;  // 1 = RTSS/External Limiter detected
};

#pragma pack(pop)