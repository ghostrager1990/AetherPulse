#include "AntiLag2Bridge.h"
#include <cstring>

AntiLag2Bridge::AntiLag2Bridge()
{
    QueryPerformanceFrequency(&m_qpcFrequency);
    QueryPerformanceCounter(&m_lastFrameQpc);
}

AntiLag2Bridge::~AntiLag2Bridge()
{
    Shutdown();
}

AntiLag2Bridge& AntiLag2Bridge::Get()
{
    static AntiLag2Bridge instance;
    return instance;
}

bool AntiLag2Bridge::Initialize(ID3D12Device* pDevice)
{
    if (m_initialized && m_pDevice == pDevice) return true;
    if (m_initialized) Shutdown();

    if (!pDevice) return false;
    m_pDevice = pDevice;
    m_pDevice->AddRef();

    m_initialized = true;
    return true;
}

void AntiLag2Bridge::Shutdown()
{
    if (m_pDevice)
    {
        m_pDevice->Release();
        m_pDevice = nullptr;
    }
    m_initialized = false;
}

void AntiLag2Bridge::TagSwapChain(IDXGISwapChain* pSwapChain, bool enabled, uint32_t targetFps)
{
    if (!pSwapChain) return;

    AntiLag2SwapchainTag tag = {};
    tag.version = 1;
    tag.flags = enabled ? 0x1 : 0x0;
    tag.targetFps = targetFps;
    tag.reserved = 0;

    // Attach private GUID data to swap chain for AMD driver & Frame Generation coordination
    pSwapChain->SetPrivateData(IID_IFfxAntiLag2Data, sizeof(AntiLag2SwapchainTag), &tag);
}

void AntiLag2Bridge::Update(bool enabled, uint32_t targetFps)
{
    m_enabled.store(enabled, std::memory_order_relaxed);
    m_targetFps.store(targetFps, std::memory_order_relaxed);
}

void AntiLag2Bridge::MarkEndOfFrameRendering()
{
    if (!m_enabled.load(std::memory_order_relaxed)) return;

    // Record timestamp for pacing and frame completion tracking
    QueryPerformanceCounter(&m_lastFrameQpc);
}
