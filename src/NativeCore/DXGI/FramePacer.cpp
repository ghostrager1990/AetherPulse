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
    QueryPerformanceCounter(&m_lastPresentQpc);

    m_hMapFile = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, L"Global\\\\AetherPulse_Pacing_IPC");
    if (m_hMapFile) {
        m_pSharedMem = (AetherPulsePacingIPC*)MapViewOfFile(m_hMapFile, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(AetherPulsePacingIPC));
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

    // Enforce cap whenever TargetFps > 0, bypass only if Uncapped (0)
    if (!m_pSharedMem || m_pSharedMem->TargetFps == 0) {
        QueryPerformanceCounter(&m_lastPresentQpc);
        return;
    }

    uint32_t targetFps = m_pSharedMem->TargetFps;
    uint32_t multiplier = m_pSharedMem->MultiplierMode;
    if (multiplier < 1) multiplier = 1;

    // Safety Floor: ensure simulation rate never starves below 20 FPS (50ms)
    uint32_t effectiveTargetFps = targetFps;
    if (multiplier > 1) {
        uint32_t minSafePresentedFps = multiplier * 20;
        if (effectiveTargetFps < minSafePresentedFps) {
            effectiveTargetFps = minSafePresentedFps;
        }
    } else {
        if (effectiveTargetFps < 15) effectiveTargetFps = 15;
    }

    double targetSliceMs = 1000.0 / (double)effectiveTargetFps;

    LARGE_INTEGER currentQpc;
    QueryPerformanceCounter(&currentQpc);

    double elapsedMs = (double)(currentQpc.QuadPart - m_lastPresentQpc.QuadPart) * 1000.0 / (double)m_qpcFreq.QuadPart;

    // Cadence discontinuity check (handles game pauses, loading screens, or sudden FPS drops gracefully)
    if (elapsedMs > 250.0 || elapsedMs <= 0.0) {
        m_lastPresentQpc = currentQpc;
        return;
    }

    double waitNeededMs = targetSliceMs - elapsedMs;

    if (waitNeededMs > 0.0) {
        // Coarse waitable timer for longer gaps (> 2ms)
        if (waitNeededMs > 2.0 && m_hWaitableTimer) {
            double sleepMs = waitNeededMs - 1.2;
            LARGE_INTEGER dueTime;
            dueTime.QuadPart = -(LONGLONG)(sleepMs * 10000.0);
            SetWaitableTimer(m_hWaitableTimer, &dueTime, 0, NULL, NULL, FALSE);
            WaitForSingleObject(m_hWaitableTimer, 15);
        }

        // Microsecond CPU spinlock for exact cadence termination
        while (true) {
            QueryPerformanceCounter(&currentQpc);
            double exactElapsedMs = (double)(currentQpc.QuadPart - m_lastPresentQpc.QuadPart) * 1000.0 / (double)m_qpcFreq.QuadPart;
            if (exactElapsedMs >= targetSliceMs) {
                break;
            }
            _mm_pause();
        }
    }

    // Anchor baseline to the completion of this presentation cycle
    QueryPerformanceCounter(&m_lastPresentQpc);
}

void FSRFramePacer::OnAfterPresent(IDXGISwapChain* pSwapChain, HRESULT presentResult) {}

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


