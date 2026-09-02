#pragma once
#include <windows.h>
#include <cstdint>
#include <cstring>
#include <string>
#include <algorithm>
#include <cmath>
#include <atomic>

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

class TelemetryPublisher
{
public:
    static TelemetryPublisher& Get()
    {
        static TelemetryPublisher instance;
        return instance;
    }

    void Initialize()
    {
        if (m_hMapFile && m_pData) return;

        SECURITY_DESCRIPTOR sd;
        InitializeSecurityDescriptor(&sd, SECURITY_DESCRIPTOR_REVISION);
        SetSecurityDescriptorDacl(&sd, TRUE, NULL, FALSE);

        SECURITY_ATTRIBUTES sa;
        sa.nLength = sizeof(sa);
        sa.lpSecurityDescriptor = &sd;
        sa.bInheritHandle = FALSE;

        const DWORD bufferSize = static_cast<DWORD>(sizeof(AetherTelemetryData));

        m_hMapFile = CreateFileMappingW(
            INVALID_HANDLE_VALUE, &sa, PAGE_READWRITE, 0, bufferSize,
            L"Global\\AetherPulseSharedMem"
        );

        if (!m_hMapFile)
        {
            m_hMapFile = CreateFileMappingW(
                INVALID_HANDLE_VALUE, &sa, PAGE_READWRITE, 0, bufferSize,
                L"Local\\AetherPulseSharedMem"
            );
        }

        if (!m_hMapFile)
        {
            m_hMapFile = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, L"Global\\AetherPulseSharedMem");
            if (!m_hMapFile)
            {
                m_hMapFile = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, L"Local\\AetherPulseSharedMem");
            }
            if (!m_hMapFile)
            {
                m_hMapFile = OpenFileMappingW(FILE_MAP_READ | FILE_MAP_WRITE, FALSE, L"Global\\AetherPulseSharedMem");
            }
        }

        if (m_hMapFile)
        {
            m_pData = static_cast<AetherTelemetryData*>(MapViewOfFile(m_hMapFile, FILE_MAP_ALL_ACCESS, 0, 0, bufferSize));
            if (!m_pData)
            {
                m_pData = static_cast<AetherTelemetryData*>(MapViewOfFile(m_hMapFile, FILE_MAP_WRITE | FILE_MAP_READ, 0, 0, bufferSize));
            }

            if (m_pData)
            {
                std::memset(m_pData, 0, bufferSize);
                m_pData->Sequence = 0;
                m_pData->StructVersion = 1;
                m_pData->FrameIndex = 0;
                m_pData->CurrentFps = 0.0f;
                m_pData->AverageFps = 0.0f;
                m_pData->FrameTimeMs = 0.0f;
                m_pData->PacingJitterMs = 0.0f;

                wchar_t exePath[MAX_PATH] = { 0 };
                GetModuleFileNameW(NULL, exePath, MAX_PATH);
                std::wstring fullPath(exePath);
                size_t slashPos = fullPath.find_last_of(L"\\/");
                std::wstring exeName = (slashPos != std::wstring::npos) ? fullPath.substr(slashPos + 1) : fullPath;
                int len = WideCharToMultiByte(CP_ACP, 0, exeName.c_str(), -1, m_pData->RawGameTitle, sizeof(m_pData->RawGameTitle) - 1, NULL, NULL);
                if (len > 0) m_pData->RawGameTitle[len] = '\0';
                OutputDebugStringA("[AetherPulse] Telemetry Shared Memory buffer created and initialized successfully.\n");
            }
            else
            {
                OutputDebugStringA("[AetherPulse] ERROR: MapViewOfFile failed.\n");
            }
        }
        else
        {
            OutputDebugStringA("[AetherPulse] ERROR: CreateFileMappingW failed.\n");
        }

        QueryPerformanceFrequency(&m_qpcFreq);
        QueryPerformanceCounter(&m_lastQpc);
    }

    void OnPresent(bool isPacingActive = true, bool isRayRegenActive = true, uint32_t denoiserFlags = 0)
    {
        if (!m_pData) Initialize();
        if (!m_pData) return;

        LARGE_INTEGER currentQpc;
        QueryPerformanceCounter(&currentQpc);

        double deltaMs = 0.0;
        if (m_qpcFreq.QuadPart > 0 && m_lastQpc.QuadPart > 0)
        {
            deltaMs = static_cast<double>(currentQpc.QuadPart - m_lastQpc.QuadPart) * 1000.0 / static_cast<double>(m_qpcFreq.QuadPart);
        }

        // Deduplication Guard: Ignore impossible sub-0.1ms frame deltas from re-entrant Present calls
        if (deltaMs < 0.10 && m_lastQpc.QuadPart > 0)
        {
            return;
        }

        m_lastQpc = currentQpc;

        float currentFps = 0.0f;
        float ftMs = 0.0f;
        float jitterPct = 0.0f;

        if (deltaMs > 0.001 && deltaMs < 1000.0)
        {
            currentFps = static_cast<float>(1000.0 / deltaMs);
            ftMs = static_cast<float>(deltaMs);

            if (m_runningAvgFps <= 0.0f)
                m_runningAvgFps = currentFps;
            else
                m_runningAvgFps = 0.95f * m_runningAvgFps + 0.05f * currentFps;

            if (m_lastDeltaMs > 0.0)
            {
                float jitter = static_cast<float>(std::abs(deltaMs - m_lastDeltaMs) / deltaMs * 100.0);
                jitterPct = (std::min)(jitter, 100.0f);
            }
            m_lastDeltaMs = deltaMs;
        }

        // Lock-Free SeqLock: increment sequence to odd (writer active)
        InterlockedIncrement(reinterpret_cast<volatile LONG*>(&m_pData->Sequence));
        std::atomic_thread_fence(std::memory_order_release);

        m_pData->StructVersion = 1;
        m_pData->FrameIndex = ++m_frameCounter;
        if (ftMs > 0.0f)
        {
            m_pData->CurrentFps = currentFps;
            m_pData->AverageFps = m_runningAvgFps;
            m_pData->FrameTimeMs = ftMs;
            m_pData->PacingJitterMs = jitterPct;
        }
        m_pData->IsPacerActive = isPacingActive ? 1 : 0;
        m_pData->IsRayRegenActive = isRayRegenActive ? 1 : 0;
        m_pData->ActiveDenoiserFlags = denoiserFlags;
        m_pData->IsExternalLimiterActive = (GetModuleHandleW(L"RTSSHooks64.dll") != nullptr ||
                                           GetModuleHandleW(L"RTSSHooks.dll") != nullptr ||
                                           GetModuleHandleW(L"SpecialK64.dll") != nullptr ||
                                           GetModuleHandleW(L"SpecialK32.dll") != nullptr) ? 1 : 0;

        // Lock-Free SeqLock: memory barrier and increment sequence to even (writer done)
        std::atomic_thread_fence(std::memory_order_release);
        InterlockedIncrement(reinterpret_cast<volatile LONG*>(&m_pData->Sequence));
    }

    void Shutdown()
    {
        if (m_pData) { UnmapViewOfFile(m_pData); m_pData = nullptr; }
        if (m_hMapFile) { CloseHandle(m_hMapFile); m_hMapFile = nullptr; }
    }

private:
    HANDLE m_hMapFile = nullptr;
    AetherTelemetryData* m_pData = nullptr;
    LARGE_INTEGER m_qpcFreq = { 0 };
    LARGE_INTEGER m_lastQpc = { 0 };
    double m_lastDeltaMs = 0.0;
    float m_runningAvgFps = 0.0f;
    uint32_t m_frameCounter = 0;
};
