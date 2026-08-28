#pragma once

#include <windows.h>
#include <d3d12.h>
#include <dxgi.h>
#include <cstdint>
#include <atomic>

// AMD FidelityFX Anti-Lag 2 DirectX 12 Coordination GUID
// {5083ae5b-8070-4fca-8ee5-3582dd367d13}
static const GUID IID_IFfxAntiLag2Data = 
    { 0x5083ae5b, 0x8070, 0x4fca, { 0x8e, 0xe5, 0x35, 0x82, 0xdd, 0x36, 0x7d, 0x13 } };

#pragma pack(push, 1)
struct AntiLag2SwapchainTag
{
    uint32_t version;         // Version 1
    uint32_t flags;           // Mode flags (e.g. 1 = enabled, 2 = FG active)
    uint32_t targetFps;       // Target presentation FPS cap
    uint32_t reserved;
};
#pragma pack(pop)

class AntiLag2Bridge
{
public:
    static AntiLag2Bridge& Get();

    bool Initialize(ID3D12Device* pDevice);
    void Shutdown();

    // Tags the swap chain private data for driver / frame gen coordination
    void TagSwapChain(IDXGISwapChain* pSwapChain, bool enabled, uint32_t targetFps);

    // Call prior to CPU simulation / input processing
    void Update(bool enabled, uint32_t targetFps);

    // Call immediately before Present() on the render thread
    void MarkEndOfFrameRendering();

    bool IsActive() const { return m_enabled; }

private:
    AntiLag2Bridge();
    ~AntiLag2Bridge();

    bool                  m_initialized = false;
    ID3D12Device*         m_pDevice = nullptr;
    std::atomic<bool>     m_enabled = true;
    std::atomic<uint32_t> m_targetFps = 0;
    LARGE_INTEGER         m_lastFrameQpc = {};
    LARGE_INTEGER         m_qpcFrequency = {};
};
