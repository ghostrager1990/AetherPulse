#include "FramePacer.h"
#include <algorithm>
#include <emmintrin.h>
#include <immintrin.h>
#include <timeapi.h>

FSRFramePacer& FSRFramePacer::GetInstance() {
    static FSRFramePacer instance;
    return instance;
}

extern "C" __declspec(dllexport) FSRFramePacer* GetAetherPulsePacer() {
    return &FSRFramePacer::GetInstance();
}

bool FSRFramePacer::Initialize() {
    if (m_initialized) return true;

    timeBeginPeriod(1);
    QueryPerformanceFrequency(&m_qpcFreq);
    
    LARGE_INTEGER cur;
    QueryPerformanceCounter(&cur);
    m_lastPresentQpc = cur;
    m_lastRealFrameQpc = cur;

    m_hMapFile = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, L"Global\\AetherPulse_Pacing_IPC");
    if (!m_hMapFile) m_hMapFile = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, L"Local\\AetherPulse_Pacing_IPC");
    if (!m_hMapFile) m_hMapFile = OpenFileMappingW(FILE_MAP_READ | FILE_MAP_WRITE, FALSE, L"Global\\AetherPulse_Pacing_IPC");
    if (!m_hMapFile) m_hMapFile = OpenFileMappingW(FILE_MAP_READ, FALSE, L"Global\\AetherPulse_Pacing_IPC");
    if (!m_hMapFile) m_hMapFile = OpenFileMappingW(FILE_MAP_READ, FALSE, L"Local\\AetherPulse_Pacing_IPC");

    if (m_hMapFile) {
        m_pSharedMem = (AetherPulsePacingIPC*)MapViewOfFile(m_hMapFile, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(AetherPulsePacingIPC));
        if (!m_pSharedMem) {
            m_pSharedMem = (AetherPulsePacingIPC*)MapViewOfFile(m_hMapFile, FILE_MAP_READ, 0, 0, sizeof(AetherPulsePacingIPC));
        }
        if (m_pSharedMem) {
            m_pSharedMem->IsHookActive = 1;
        }
    }

    m_hWaitableTimer = CreateWaitableTimerExW(NULL, NULL, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
    if (!m_hWaitableTimer) {
        m_hWaitableTimer = CreateWaitableTimerW(NULL, FALSE, NULL);
    }

    m_initialized = true;
    return true;
}

void FSRFramePacer::OnBeforePresent(IDXGISwapChain* pSwapChain, UINT SyncInterval, UINT Flags) {
    if (!m_initialized) Initialize();

    // Re-verify IPC connection if disconnected
    if (!m_pSharedMem || !m_hMapFile) {
        if (m_hMapFile) { CloseHandle(m_hMapFile); m_hMapFile = NULL; }
        m_hMapFile = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, L"Global\\AetherPulse_Pacing_IPC");
        if (!m_hMapFile) m_hMapFile = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, L"Local\\AetherPulse_Pacing_IPC");
        if (m_hMapFile) {
            m_pSharedMem = (AetherPulsePacingIPC*)MapViewOfFile(m_hMapFile, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(AetherPulsePacingIPC));
            if (m_pSharedMem) m_pSharedMem->IsHookActive = 1;
        }
    }

    // Check for external limiter modules (RTSS, SpecialK)
    static DWORD s_lastLimiterCheck = 0;
    DWORD currentTick = GetTickCount();
    if (currentTick - s_lastLimiterCheck > 1000) {
        s_lastLimiterCheck = currentTick;
        if (GetModuleHandleW(L"RTSSHooks64.dll") != nullptr ||
            GetModuleHandleW(L"RTSSHooks.dll") != nullptr ||
            GetModuleHandleW(L"SpecialK64.dll") != nullptr ||
            GetModuleHandleW(L"SpecialK32.dll") != nullptr) {
            m_externalLimiterActive.store(true);
        }
    }

    if (m_pSharedMem) {
        m_pSharedMem->IsExternalLimiterActive = m_externalLimiterActive.load() ? 1 : 0;
    }

    LARGE_INTEGER currentQpc;
    QueryPerformanceCounter(&currentQpc);

    // Instant Zero-Latency Bypass for Uncapped Native (1X), Disabled Pacing, or Active External Limiter (RTSS pass-through)
    if (!m_pSharedMem || m_pSharedMem->EnablePacing == 0 || m_pSharedMem->MultiplierMode <= 1 || m_externalLimiterActive.load()) {
        m_lastPresentQpc = currentQpc;
        return;
    }

    uint32_t multiplier = m_pSharedMem->MultiplierMode;
    if (multiplier < 2) multiplier = 2;

    // Sub-frame Cadence Alignment for Frame Generation (50/50 interval pacing):
    if (m_smoothedBaseIntervalQpc > 0 && m_isInterpolatedFrame) {
        int64_t subFrameIntervalQpc = m_smoothedBaseIntervalQpc / multiplier;
        int64_t targetQpc = m_lastPresentQpc.QuadPart + subFrameIntervalQpc;
        int64_t waitQpc = targetQpc - currentQpc.QuadPart;

        if (waitQpc > 0) {
            double waitMs = (double)waitQpc * 1000.0 / (double)m_qpcFreq.QuadPart;
            double maxWaitMs = ((double)subFrameIntervalQpc * 1000.0 / (double)m_qpcFreq.QuadPart) * 0.8;
            if (waitMs > maxWaitMs) waitMs = maxWaitMs;

            float spinThresholdMs = m_pSharedMem->SpinWaitThresholdMs > 0.5f ? m_pSharedMem->SpinWaitThresholdMs : 2.0f;
            if (waitMs > (double)spinThresholdMs && m_hWaitableTimer) {
                double sleepMs = waitMs - 1.0;
                if (sleepMs > 0.5) {
                    LARGE_INTEGER dueTime;
                    dueTime.QuadPart = -(LONGLONG)(sleepMs * 10000.0);
                    SetWaitableTimer(m_hWaitableTimer, &dueTime, 0, NULL, NULL, FALSE);
                    WaitForSingleObject(m_hWaitableTimer, (DWORD)(sleepMs + 5.0));
                }
            }

            while (true) {
                QueryPerformanceCounter(&currentQpc);
                if (currentQpc.QuadPart >= targetQpc) {
                    break;
                }
                _mm_pause();
            }
        }
    }
}

void FSRFramePacer::OnAfterPresent(IDXGISwapChain* pSwapChain, HRESULT presentResult) {
    LARGE_INTEGER nowQpc;
    QueryPerformanceCounter(&nowQpc);

    double deltaMs = 0.0;
    if (m_qpcFreq.QuadPart > 0 && m_lastPresentQpc.QuadPart > 0) {
        deltaMs = (double)(nowQpc.QuadPart - m_lastPresentQpc.QuadPart) * 1000.0 / (double)m_qpcFreq.QuadPart;
    }

    if (deltaMs > 0.1 && deltaMs < 500.0) {
        // External Limiter Auto-Detection: if consecutive frames exhibit tight delta stability (<150us jitter), RTSS/driver limiter is active
        if (m_lastDeltaMs > 0.001) {
            double diff = std::abs(deltaMs - m_lastDeltaMs);
            if (diff < 0.150) {
                if (++m_tightClampStreak > 8)
                    m_externalLimiterActive.store(true);
            } else if (diff > 0.750) {
                m_tightClampStreak = 0;
                m_externalLimiterActive.store(false);
            }
        }

        m_isInterpolatedFrame = !m_isInterpolatedFrame;
        if (!m_isInterpolatedFrame) {
            int64_t fullIntervalQpc = nowQpc.QuadPart - m_lastRealFrameQpc.QuadPart;
            if (fullIntervalQpc > 0 && fullIntervalQpc < m_qpcFreq.QuadPart) {
                double alpha = 0.050;
                if (!m_pSharedMem || m_pSharedMem->AutoEma != 0) {
                    double currentFps = deltaMs > 0.001 ? (1000.0 / deltaMs) : 60.0;
                    if (currentFps <= 40.0) alpha = 0.05;
                    else if (currentFps >= 144.0) alpha = 0.22;
                    else {
                        double t = (currentFps - 40.0) / (144.0 - 40.0);
                        alpha = 0.05 + t * (0.22 - 0.05);
                    }
                } else {
                    alpha = (m_pSharedMem->ManualEmaAlpha > 0.001f) ? (double)m_pSharedMem->ManualEmaAlpha : 0.050;
                }

                if (m_smoothedBaseIntervalQpc <= 0)
                    m_smoothedBaseIntervalQpc = fullIntervalQpc;
                else
                    m_smoothedBaseIntervalQpc = (int64_t)((1.0 - alpha) * (double)m_smoothedBaseIntervalQpc + alpha * (double)fullIntervalQpc);
            }
            m_lastRealFrameQpc = nowQpc;
        }

        float cadenceRatio = 0.50f;
        if (m_lastDeltaMs > 0.001) {
            double sumDelta = deltaMs + m_lastDeltaMs;
            if (sumDelta > 0.001) {
                cadenceRatio = (float)(deltaMs / sumDelta);
            }
        }

        float subFrameVarianceUs = 0.0f;
        if (m_lastDeltaMs > 0.001) {
            subFrameVarianceUs = (float)(std::abs(deltaMs - m_lastDeltaMs) * 1000.0);
        }

        m_lastDeltaMs = deltaMs;
        m_currentFps.store((float)(1000.0 / deltaMs));
        m_frameTimeMs.store((float)deltaMs);
        m_cadenceRatio.store(cadenceRatio);
        m_subFrameVarianceUs.store(subFrameVarianceUs);
    }

    m_lastPresentQpc = nowQpc;
}

void FSRFramePacer::Shutdown() {
    if (m_pSharedMem) {
        m_pSharedMem->IsHookActive = 0;
        UnmapViewOfFile(m_pSharedMem);
        m_pSharedMem = nullptr;
    }
    if (m_hMapFile) {
        CloseHandle(m_hMapFile);
        m_hMapFile = NULL;
    }
    if (m_hWaitableTimer) {
        CloseHandle(m_hWaitableTimer);
        m_hWaitableTimer = NULL;
    }
    timeEndPeriod(1);
    m_initialized = false;
}

