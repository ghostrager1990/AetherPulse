#pragma once

#include <cstdint>
#include <string>

enum class FrameGenMultiplier : uint32_t
{
    Adaptive = 0,
    x1 = 1,
    x2 = 2,
    x3 = 3,
    x4 = 4,
    x5 = 5,
    x6 = 6
};

struct PacingConfig
{
    bool                enablePacing = true;
    bool                enableHalfIntervalPacing = true; // Multi-interval pacing for frame generation
    bool                enableAntiLag2 = true;           // AMD Radeon Anti-Lag 2 SDK integration
    uint32_t            targetFpsCap = 0;                // 0 = unbounded / automatic EMA tracking
    FrameGenMultiplier  multiplierMode = FrameGenMultiplier::Adaptive; // 1x to 6x or Adaptive
    uint32_t            targetFps = 180;                 // Adaptive Target FPS (60 - 360)
    float               emaAlpha = 0.125f;               // 16-frame rolling weight alpha
    uint32_t            spinYieldMicroseconds = 500;     // Sub-millisecond spin-wait threshold
    bool                forceFlipDiscard = true;         // Force DXGI_SWAP_EFFECT_FLIP_DISCARD
    uint32_t            maxFrameLatency = 1;             // DXGI maximum frame latency
    bool                hudProtection = true;            // HUD Preservation Mask for 2D UI elements
};

struct DenoiserConfig
{
    bool     enableRayRegen = true;
    bool     neuralRadianceCache = true;       // Neural Radiance Caching (NRC) for multi-bounce GI
    bool     denoiseReflections = true;        // Reflection denoising pass
    bool     denoiseShadows = true;            // Shadow / AO denoising pass
    bool     glossyRadianceFilter = true;      // Glossy specular radiance filtering
    float    roughnessThreshold = 0.5f;
    uint32_t spatialFilterPasses = 2;
    float    temporalWeight = 0.85f;
    float    depthSigma = 1.0f;
    float    normalSigma = 64.0f;
    bool     forceAutoExposure = true;         // FSR 4 raw radiance exposure correction
    bool     colorSpaceCorrect = true;         // Non-linear gamma and color space correction
    bool     enableDisocclusionFilter = true;  // Disocclusion history optimization
};

struct FSRConfig
{
    std::string mode = "Quality";
    bool     nativeAA = false;                 // Native AA (FSR Native resolution render pass)
    bool     reactiveMask = true;              // Reactive mask optimization for fast HUD/particles
    bool     enableRCASOverride = true;
    float    sharpness = 0.35f;
    bool     autoLODBias = true;
    float    textureLODBias = -0.58f;
    float    reactiveMaskSensitivity = 0.10f;
    uint32_t clampMinRenderScale = 67;
};

struct TelemetryConfig
{
    bool     enableSharedMemory = true;
    uint32_t updateIntervalMs = 16;
};

struct ChainingConfig
{
    bool         enableProxyChaining = true;
    std::wstring originalDllPath; // e.g. L"dxgi_chain.dll", L"ReShade64.dll", L"OptiScaler.dll"
};

struct AetherConfig
{
    PacingConfig    pacing;
    DenoiserConfig  denoiser;
    FSRConfig       fsr;
    TelemetryConfig telemetry;
    ChainingConfig  chaining;

    static AetherConfig& Get();
    bool Load(const std::wstring& configPath = L"");
};
