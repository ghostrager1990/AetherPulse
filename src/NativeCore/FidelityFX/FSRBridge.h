#pragma once

#include <windows.h>
#include <d3d12.h>
#include <cstdint>

struct FSRConstants
{
    uint32_t renderWidth;
    uint32_t renderHeight;
    uint32_t displayWidth;
    uint32_t displayHeight;
    float    jitterOffsetX;
    float    jitterOffsetY;
    float    sharpness;
    float    sharpnessAttenuation;
};

class FSRBridge
{
public:
    static FSRBridge& Get();

    bool Initialize(ID3D12Device* pDevice);
    void Shutdown();

    bool DispatchUpscale(
        ID3D12GraphicsCommandList* pCmdList,
        ID3D12Resource* pInputColor,
        ID3D12Resource* pOutputColor,
        ID3D12Resource* pDepth,
        ID3D12Resource* pMotionVectors,
        uint32_t renderWidth,
        uint32_t renderHeight,
        uint32_t displayWidth,
        uint32_t displayHeight,
        float jitterX,
        float jitterY,
        float sharpness
    );

    bool IsInitialized() const { return m_initialized; }

private:
    FSRBridge() = default;
    ~FSRBridge() { Shutdown(); }

    FSRBridge(const FSRBridge&) = delete;
    FSRBridge& operator=(const FSRBridge&) = delete;

    bool m_initialized = false;
    ID3D12Device* m_pDevice = nullptr;
    HMODULE m_hUpscalerDll = nullptr;
    HMODULE m_hFrameGenDll = nullptr;
    void* m_pfnFsrDispatch = nullptr;
    void* m_pfnFsrCreate = nullptr;
    void* m_pfnFsrDestroy = nullptr;
};
