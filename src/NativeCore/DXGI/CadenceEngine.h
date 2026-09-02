#pragma once

#include <d3d12.h>
#include <dxgi1_6.h>
#include <windows.h>
#include <atomic>

class CadenceEngine {
private:
    HANDLE m_hWaitableTimer = nullptr;
    ID3D12Fence *m_pFlushFence = nullptr;
    UINT64 m_fenceValue = 0;

    std::atomic<double> m_targetFrameTimeMs{0.0}; // 0 = Uncapped
    std::atomic<bool> m_needsQueueFlush{false};

    LARGE_INTEGER m_qpcFrequency;
    LARGE_INTEGER m_lastFrameQpc;
    double m_emaDelta = 0.0;

public:
    static CadenceEngine& Get();

    CadenceEngine();
    ~CadenceEngine();

    void SetTargetFPS(double targetFps);
    void OnPresentPacing(ID3D12CommandQueue *pCommandQueue);
    static double ComputeAdaptiveEmaAlpha(double frametimeMs) {
        double currentFps = frametimeMs > 0.001 ? (1000.0 / frametimeMs) : 60.0;
        if (currentFps <= 40.0) return 0.05;
        if (currentFps >= 144.0) return 0.22;
        double t = (currentFps - 40.0) / (144.0 - 40.0);
        return 0.05 + t * (0.22 - 0.05);
    }
};
