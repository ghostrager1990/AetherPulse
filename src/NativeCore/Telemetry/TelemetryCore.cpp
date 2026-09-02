#include "TelemetryCore.h"
#include "../Shared/Config.h"
#include <cstring>
#include <string>
#include <algorithm>
#include <cmath>
#include <vector>

TelemetryCore& TelemetryCore::Get()
{
    static TelemetryCore instance;
    return instance;
}

TelemetryCore::TelemetryCore()
{
    QueryPerformanceFrequency(&m_qpcFrequency);
    QueryPerformanceCounter(&m_lastPresentQpc);
}

TelemetryCore::~TelemetryCore()
{
    Shutdown();
}

void TelemetryCore::Initialize()
{
    if (m_hMapFile && m_pData && m_hRtssMapFile && m_pRtssHeader) return;

    SECURITY_DESCRIPTOR sd;
    InitializeSecurityDescriptor(&sd, SECURITY_DESCRIPTOR_REVISION);
    SetSecurityDescriptorDacl(&sd, TRUE, NULL, FALSE);

    SECURITY_ATTRIBUTES sa;
    sa.nLength = sizeof(sa);
    sa.lpSecurityDescriptor = &sd;
    sa.bInheritHandle = FALSE;

    DWORD pid = GetCurrentProcessId();

    // 1. Initialize AetherPulse Shared Memory
    if (!m_hMapFile)
    {
        const DWORD bufferSize = static_cast<DWORD>(sizeof(AetherTelemetryData));

        m_hMapFile = CreateFileMappingW(
            INVALID_HANDLE_VALUE, &sa, PAGE_READWRITE, 0, bufferSize,
            L"Local\\AetherPulseSharedMem"
        );
        if (!m_hMapFile) m_hMapFile = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, L"Local\\AetherPulseSharedMem");
        if (!m_hMapFile) m_hMapFile = CreateFileMappingW(INVALID_HANDLE_VALUE, &sa, PAGE_READWRITE, 0, bufferSize, L"Global\\AetherPulseSharedMem");
        if (!m_hMapFile) m_hMapFile = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, L"Global\\AetherPulseSharedMem");
        if (!m_hMapFile) m_hMapFile = OpenFileMappingW(FILE_MAP_READ | FILE_MAP_WRITE, FALSE, L"Local\\AetherPulseSharedMem");

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
                m_pData->StructVersion = 0xAEE1;
                m_pData->FrameIndex = 1;
                m_pData->CurrentFps = 0.0f;
                m_pData->AverageFps = 0.0f;
                m_pData->FrameTimeMs = 0.0f;
                m_pData->PacingJitterMs = 0.0f;
                m_pData->DroppedFrames = 0;

                wchar_t exePath[MAX_PATH] = { 0 };
                GetModuleFileNameW(NULL, exePath, MAX_PATH);
                std::wstring fullPath(exePath);
                size_t slashPos = fullPath.find_last_of(L"\\/");
                std::wstring exeName = (slashPos != std::wstring::npos) ? fullPath.substr(slashPos + 1) : fullPath;
                int len = WideCharToMultiByte(CP_ACP, 0, exeName.c_str(), -1, m_pData->RawGameTitle, sizeof(m_pData->RawGameTitle) - 1, NULL, NULL);
                if (len > 0) m_pData->RawGameTitle[len] = '\0';

                OutputDebugStringA("[AetherPulse] TelemetryCore AetherPulseSharedMem initialized.\n");
            }
        }
    }

    // 2. Initialize Standard RTSS Shared Memory
    if (!m_hRtssMapFile)
    {
        const DWORD rtssSize = sizeof(RTSS_SHARED_MEMORY) + 256 * sizeof(RTSS_SHARED_MEMORY_APP_ENTRY);

        m_hRtssMapFile = CreateFileMappingW(
            INVALID_HANDLE_VALUE, &sa, PAGE_READWRITE, 0, rtssSize,
            L"Local\\RTSSSharedMemory"
        );
        if (!m_hRtssMapFile) m_hRtssMapFile = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, L"Local\\RTSSSharedMemory");
        if (!m_hRtssMapFile) m_hRtssMapFile = CreateFileMappingW(INVALID_HANDLE_VALUE, &sa, PAGE_READWRITE, 0, rtssSize, L"Global\\RTSSSharedMemory");
        if (!m_hRtssMapFile) m_hRtssMapFile = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, L"Global\\RTSSSharedMemory");

        if (m_hRtssMapFile)
        {
            m_pRtssHeader = static_cast<RTSS_SHARED_MEMORY*>(MapViewOfFile(m_hRtssMapFile, FILE_MAP_ALL_ACCESS, 0, 0, rtssSize));
            if (!m_pRtssHeader)
            {
                m_pRtssHeader = static_cast<RTSS_SHARED_MEMORY*>(MapViewOfFile(m_hRtssMapFile, FILE_MAP_WRITE | FILE_MAP_READ, 0, 0, rtssSize));
            }

            if (m_pRtssHeader)
            {
                if (m_pRtssHeader->dwSignature != RTSS_SHARED_MEMORY_SIGNATURE)
                {
                    std::memset(m_pRtssHeader, 0, rtssSize);
                    m_pRtssHeader->dwSignature = RTSS_SHARED_MEMORY_SIGNATURE;
                    m_pRtssHeader->dwVersion = RTSS_SHARED_MEMORY_VERSION;
                    m_pRtssHeader->dwAppEntrySize = sizeof(RTSS_SHARED_MEMORY_APP_ENTRY);
                    m_pRtssHeader->dwAppArrOffset = sizeof(RTSS_SHARED_MEMORY);
                    m_pRtssHeader->dwAppArrSize = 256;
                }

                auto* appEntries = reinterpret_cast<RTSS_SHARED_MEMORY_APP_ENTRY*>(
                    reinterpret_cast<uint8_t*>(m_pRtssHeader) + m_pRtssHeader->dwAppArrOffset
                );

                // Find or allocate slot for current process
                RTSS_SHARED_MEMORY_APP_ENTRY* targetSlot = nullptr;
                for (DWORD i = 0; i < m_pRtssHeader->dwAppArrSize; ++i)
                {
                    if (appEntries[i].dwProcessId == pid)
                    {
                        targetSlot = &appEntries[i];
                        break;
                    }
                    if (!targetSlot && appEntries[i].dwProcessId == 0)
                    {
                        targetSlot = &appEntries[i];
                    }
                }

                if (targetSlot)
                {
                    m_pRtssAppEntry = targetSlot;
                    m_pRtssAppEntry->dwProcessId = pid;
                    m_pRtssAppEntry->dwFlags = 1;
                    m_pRtssAppEntry->dwFrames = 1;
                    m_pRtssAppEntry->dwFramerate = 0;
                    m_pRtssAppEntry->dwFrameTime = 0;
                    m_pRtssAppEntry->dwFramerateLimit = AetherConfig::Get().pacing.targetFpsCap;

                    wchar_t exePath[MAX_PATH] = { 0 };
                    GetModuleFileNameW(NULL, exePath, MAX_PATH);
                    std::wstring fullPath(exePath);
                    size_t slashPos = fullPath.find_last_of(L"\\/");
                    std::wstring exeName = (slashPos != std::wstring::npos) ? fullPath.substr(slashPos + 1) : fullPath;
                    WideCharToMultiByte(CP_ACP, 0, exeName.c_str(), -1, m_pRtssAppEntry->szName, MAX_PATH - 1, NULL, NULL);

                    OutputDebugStringA("[AetherPulse] TelemetryCore RTSSSharedMemory app entry attached successfully.\n");
                }
            }
        }
    }

    QueryPerformanceFrequency(&m_qpcFrequency);
    QueryPerformanceCounter(&m_lastPresentQpc);
}

void TelemetryCore::Shutdown()
{
    if (m_pRtssAppEntry)
    {
        m_pRtssAppEntry->dwProcessId = 0;
        m_pRtssAppEntry = nullptr;
    }
    if (m_pRtssHeader)
    {
        UnmapViewOfFile(m_pRtssHeader);
        m_pRtssHeader = nullptr;
    }
    if (m_hRtssMapFile)
    {
        CloseHandle(m_hRtssMapFile);
        m_hRtssMapFile = nullptr;
    }

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

uint32_t TelemetryCore::GetFramerateLimit() const
{
    if (m_pRtssAppEntry && m_pRtssAppEntry->dwFramerateLimit > 0)
    {
        return m_pRtssAppEntry->dwFramerateLimit;
    }
    return AetherConfig::Get().pacing.targetFpsCap;
}

float TelemetryCore::ComputeOnePercentLowFps() const
{
    if (m_historyCount == 0) return 0.0f;

    std::vector<float> sorted(m_frameTimeHistory, m_frameTimeHistory + m_historyCount);
    std::sort(sorted.begin(), sorted.end(), std::greater<float>()); // Longest frametimes first

    size_t sampleIndex = static_cast<size_t>(std::ceil(sorted.size() * 0.01f));
    sampleIndex = (std::min)(sampleIndex, sorted.size() - 1);

    float p99FrametimeMs = sorted[sampleIndex];
    return p99FrametimeMs > 0.001f ? (1000.0f / p99FrametimeMs) : 0.0f;
}

void TelemetryCore::RecordPresent()
{
    const auto& config = AetherConfig::Get();
    RecordPresent(config.pacing.enablePacing, config.denoiser.enableRayRegen, 0);
}

void TelemetryCore::RecordPresent(bool isPacerActive, bool isRayRegenActive, uint32_t denoiserFlags)
{
    if (!m_pData || !m_pRtssAppEntry) Initialize();

    LARGE_INTEGER now;
    QueryPerformanceCounter(&now);

    double deltaMs = 0.0;
    if (m_qpcFrequency.QuadPart > 0 && m_lastPresentQpc.QuadPart > 0)
    {
        deltaMs = static_cast<double>(now.QuadPart - m_lastPresentQpc.QuadPart) * 1000.0 / static_cast<double>(m_qpcFrequency.QuadPart);
    }

    if (deltaMs < 0.10 && m_lastPresentQpc.QuadPart > 0)
    {
        return;
    }

    m_lastPresentQpc = now;

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

        m_frameTimeHistory[m_historyIndex] = ftMs;
        m_historyIndex = (m_historyIndex + 1) % HISTORY_CAPACITY;
        if (m_historyCount < HISTORY_CAPACITY) m_historyCount++;

        if (ftMs > 33.4f)
        {
            m_droppedFrames++;
        }
    }

    m_frameCounter++;

    // 1. Update AetherPulse MMF
    if (m_pData)
    {
        InterlockedIncrement(reinterpret_cast<volatile LONG*>(&m_pData->Sequence));
        std::atomic_thread_fence(std::memory_order_release);

        m_pData->StructVersion = 0xAEE1;
        m_pData->FrameIndex = m_frameCounter;
        if (ftMs > 0.0f)
        {
            m_pData->CurrentFps = currentFps;
            m_pData->AverageFps = m_runningAvgFps;
            m_pData->FrameTimeMs = ftMs;
            m_pData->PacingJitterMs = jitterPct;
        }
        m_pData->DroppedFrames = m_droppedFrames;
        m_pData->IsPacerActive = isPacerActive ? 1 : 0;
        m_pData->IsRayRegenActive = isRayRegenActive ? 1 : 0;
        m_pData->ActiveDenoiserFlags = denoiserFlags;
        m_pData->CadenceRatio = 0.50f;
        m_pData->SubFrameVarianceUs = static_cast<float>(jitterPct * 10.0f);
        m_pData->RealTimeDeltaMs = ftMs;

        std::atomic_thread_fence(std::memory_order_release);
        InterlockedIncrement(reinterpret_cast<volatile LONG*>(&m_pData->Sequence));
    }

        // 2. Update RTSS Standard MMF
        if (m_pRtssAppEntry)
        {
            m_pRtssAppEntry->dwFrames = m_frameCounter;
            m_pRtssAppEntry->dwFrameTime = static_cast<DWORD>(ftMs * 1000.0f); // Microseconds
            m_pRtssAppEntry->dwFramerate = static_cast<DWORD>(currentFps * 10.0f); // Instantaneous FPS * 10
        }
    }

void TelemetryCore::UpdateLiveMetrics(float currentFps, float frameTimeMs, bool isPacerActive, bool isRayRegenActive, uint32_t denoiserFlags)
{
    if (currentFps <= 0.0f || frameTimeMs <= 0.0f) return;

    if (m_runningAvgFps <= 0.0f)
        m_runningAvgFps = currentFps;
    else
        m_runningAvgFps = 0.95f * m_runningAvgFps + 0.05f * currentFps;

    float jitterPct = 0.0f;
    if (m_lastDeltaMs > 0.0)
    {
        float jitter = static_cast<float>(std::abs((double)frameTimeMs - m_lastDeltaMs) / (double)frameTimeMs * 100.0);
        jitterPct = (std::min)(jitter, 100.0f);
    }
    m_lastDeltaMs = frameTimeMs;

    m_frameTimeHistory[m_historyIndex] = frameTimeMs;
    m_historyIndex = (m_historyIndex + 1) % HISTORY_CAPACITY;
    if (m_historyCount < HISTORY_CAPACITY) m_historyCount++;

    if (frameTimeMs > 33.4f)
    {
        m_droppedFrames++;
    }

    m_frameCounter++;

    // 1. Update AetherPulse MMF
    if (m_pData)
    {
        InterlockedIncrement(reinterpret_cast<volatile LONG*>(&m_pData->Sequence));
        std::atomic_thread_fence(std::memory_order_release);

        m_pData->StructVersion = 0xAEE1;
        m_pData->FrameIndex = m_frameCounter;
        m_pData->CurrentFps = currentFps;
        m_pData->AverageFps = m_runningAvgFps;
        m_pData->FrameTimeMs = frameTimeMs;
        m_pData->PacingJitterMs = jitterPct;
        m_pData->DroppedFrames = m_droppedFrames;
        m_pData->IsPacerActive = isPacerActive ? 1 : 0;
        m_pData->IsRayRegenActive = isRayRegenActive ? 1 : 0;
        m_pData->ActiveDenoiserFlags = denoiserFlags;

        std::atomic_thread_fence(std::memory_order_release);
        InterlockedIncrement(reinterpret_cast<volatile LONG*>(&m_pData->Sequence));
    }

    // 2. Update RTSS Standard MMF
    if (m_pRtssAppEntry)
    {
        m_pRtssAppEntry->dwFrames = m_frameCounter;
        m_pRtssAppEntry->dwFrameTime = static_cast<DWORD>(frameTimeMs * 1000.0f); // Microseconds
        m_pRtssAppEntry->dwFramerate = static_cast<DWORD>(currentFps * 10.0f); // Instantaneous FPS * 10
    }
}
