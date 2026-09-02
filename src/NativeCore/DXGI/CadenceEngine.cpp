#include "CadenceEngine.h"

CadenceEngine& CadenceEngine::Get() {
    static CadenceEngine instance;
    return instance;
}

CadenceEngine::CadenceEngine() {
    QueryPerformanceFrequency(&m_qpcFrequency);
    QueryPerformanceCounter(&m_lastFrameQpc);

    // Sub-millisecond high-resolution waitable timer handle
    m_hWaitableTimer = CreateWaitableTimerExW(
        NULL, NULL,
        CREATE_WAITABLE_TIMER_HIGH_RESOLUTION | CREATE_WAITABLE_TIMER_MANUAL_RESET,
        TIMER_ALL_ACCESS
    );
}

CadenceEngine::~CadenceEngine() {
    if (m_hWaitableTimer) {
        CloseHandle(m_hWaitableTimer);
        m_hWaitableTimer = nullptr;
    }
    if (m_pFlushFence) {
        m_pFlushFence->Release();
        m_pFlushFence = nullptr;
    }
}

void CadenceEngine::SetTargetFPS(double targetFps) {
    if (targetFps < 15.0 || targetFps > 500.0) {
        m_targetFrameTimeMs.store(0.0); // Pacing disabled / uncapped (prevents TDR watchdog stalls)
    } else {
        m_targetFrameTimeMs.store(1000.0 / targetFps);
    }

    // Trigger immediate GPU command queue flush & EMA reset
    m_needsQueueFlush.store(true);
}

void CadenceEngine::OnPresentPacing(ID3D12CommandQueue *pCommandQueue) {
    // 1. Instant Un-cap & Setting Change Flush
    if (m_needsQueueFlush.exchange(false)) {
        m_emaDelta = 0.0;
        QueryPerformanceCounter(&m_lastFrameQpc);

        if (pCommandQueue && m_pFlushFence) {
            m_fenceValue++;
            pCommandQueue->Signal(m_pFlushFence, m_fenceValue);
            if (m_pFlushFence->GetCompletedValue() < m_fenceValue) {
                HANDLE hEvent = CreateEvent(NULL, FALSE, FALSE, NULL);
                m_pFlushFence->SetEventOnCompletion(m_fenceValue, hEvent);
                WaitForSingleObject(hEvent, INFINITE);
                CloseHandle(hEvent);
            }
        }
        return;
    }

    double targetMs = m_targetFrameTimeMs.load();
    if (targetMs <= 0.0) {
        return; // Uncapped fast-path
    }

    // 2. High-Precision Cadence Loop with External Limiter Pass-Through
    LARGE_INTEGER currentQpc;
    QueryPerformanceCounter(&currentQpc);
    double elapsedMs = (double)(currentQpc.QuadPart - m_lastFrameQpc.QuadPart) * 1000.0 / (double)m_qpcFrequency.QuadPart;

    // If external limiter already clamped presentation within 0.25ms of target, bypass to prevent contention
    if (elapsedMs >= targetMs - 0.25) {
        QueryPerformanceCounter(&m_lastFrameQpc);
        return;
    }

    if (elapsedMs < targetMs) {
        double waitTimeMs = targetMs - elapsedMs;

        if (m_hWaitableTimer && waitTimeMs > 0.5) {
            LARGE_INTEGER dueTime;
            dueTime.QuadPart = -static_cast<LONGLONG>((waitTimeMs - 0.2) * 10000.0);
            SetWaitableTimer(m_hWaitableTimer, &dueTime, 0, NULL, NULL, 0);
            WaitForSingleObject(m_hWaitableTimer, INFINITE);
        }

        // Spin-lock fine-tune for sub-millisecond precision
        do {
            QueryPerformanceCounter(&currentQpc);
            elapsedMs = (double)(currentQpc.QuadPart - m_lastFrameQpc.QuadPart) * 1000.0 / (double)m_qpcFrequency.QuadPart;
        } while (elapsedMs < targetMs);
    }

    QueryPerformanceCounter(&m_lastFrameQpc);
}