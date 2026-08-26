#pragma once

#include <cstdint>
#include <windows.h>
#include <string_view>
#include <cstring>
#include <atomic>

#pragma pack(push, 1)
struct AetherTelemetryData
{
    uint32_t structVersion;         // Expected: 1
    float    currentFps;            // Current calculated FPS
    float    frameTimeMs;           // Current frame time in milliseconds
    float    pacingJitterMs;        // Pacing deviation / jitter in milliseconds
    bool     isPacerActive;         // Whether frame pacer hook is active
    bool     isRayRegenActive;      // Whether FidelityFX ray regeneration is active
    uint32_t activeDenoiserFlags;   // Bitfield: active denoiser passes
    uint32_t droppedFrames;         // Count of pacing deadline misses
    char     activeGameTitle[128];  // Detected active process / window title
};
#pragma pack(pop)

static_assert(sizeof(AetherTelemetryData) == (4 + 4 + 4 + 4 + 1 + 1 + 4 + 4 + 128), "AetherTelemetryData pack(push, 1) layout mismatch");

constexpr std::string_view AETHER_TELEMETRY_MAP_NAME = "Local\\AetherPulseTelemetry";
constexpr uint32_t AETHER_TELEMETRY_VERSION = 1;

class AetherTelemetryServer
{
public:
    static AetherTelemetryServer& Get()
    {
        static AetherTelemetryServer instance;
        return instance;
    }

    bool Initialize()
    {
        if (m_pData) return true;

        m_hMapFile = CreateFileMappingA(
            INVALID_HANDLE_VALUE,
            nullptr,
            PAGE_READWRITE,
            0,
            sizeof(AetherTelemetryData),
            AETHER_TELEMETRY_MAP_NAME.data()
        );

        if (!m_hMapFile)
        {
            return false;
        }

        m_pData = static_cast<AetherTelemetryData*>(
            MapViewOfFile(m_hMapFile, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(AetherTelemetryData))
        );

        if (!m_pData)
        {
            CloseHandle(m_hMapFile);
            m_hMapFile = nullptr;
            return false;
        }

        std::memset(m_pData, 0, sizeof(AetherTelemetryData));
        m_pData->structVersion = AETHER_TELEMETRY_VERSION;

        char exePath[MAX_PATH] = { 0 };
        GetModuleFileNameA(nullptr, exePath, MAX_PATH);
        const char* filename = strrchr(exePath, '\\');
        if (filename)
        {
            filename++;
        }
        else
        {
            filename = exePath;
        }
        strncpy_s(m_pData->activeGameTitle, filename, sizeof(m_pData->activeGameTitle) - 1);

        return true;
    }

    void UpdateTelemetry(float fps, float frameTimeMs, float jitterMs, bool pacerActive, bool rayRegenActive, uint32_t denoiserFlags, uint32_t droppedFrames)
    {
        if (!m_pData) return;

        m_pData->structVersion = AETHER_TELEMETRY_VERSION;
        m_pData->currentFps = fps;
        m_pData->frameTimeMs = frameTimeMs;
        m_pData->pacingJitterMs = jitterMs;
        m_pData->isPacerActive = pacerActive;
        m_pData->isRayRegenActive = rayRegenActive;
        m_pData->activeDenoiserFlags = denoiserFlags;
        m_pData->droppedFrames = droppedFrames;
    }

    void Shutdown()
    {
        if (m_pData)
        {
            UnmapViewOfFile(m_pData);
            m_pData = nullptr;
        }
        if (m_hMapFile)
        {
            CloseHandle(m_hMapFile);
            m_hMapFile = nullptr;
        }
    }

private:
    AetherTelemetryServer() = default;
    ~AetherTelemetryServer() { Shutdown(); }

    AetherTelemetryServer(const AetherTelemetryServer&) = delete;
    AetherTelemetryServer& operator=(const AetherTelemetryServer&) = delete;

    HANDLE m_hMapFile = nullptr;
    AetherTelemetryData* m_pData = nullptr;
};
