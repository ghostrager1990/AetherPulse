#include "FSRBridge.h"
#include "RCASBridge.h"
#include "../Shared/Config.h"
#include <string>

typedef int32_t FfxReturnCode;
typedef void* FfxUpscaleContext;

typedef FfxReturnCode (*PFN_ffxFsrContextDispatch)(
    void* pContext,
    const void* pDispatchParams
);

typedef FfxReturnCode (*PFN_ffxFsrContextCreate)(
    void* pContext,
    const void* pCreateParams
);

typedef FfxReturnCode (*PFN_ffxFsrContextDestroy)(
    void* pContext
);

static uint32_t g_lastTrackedCap = 0;
static int g_resetSignalFrames = 0;

FSRBridge& FSRBridge::Get()
{
    static FSRBridge s_instance;
    return s_instance;
}

bool FSRBridge::Initialize(ID3D12Device* pDevice)
{
    if (!pDevice) return false;
    m_pDevice = pDevice;
    m_pDevice->AddRef();

    if (!m_hUpscalerDll)
    {
        m_hUpscalerDll = LoadLibraryW(L"amd_fidelityfx_upscaler_dx12.dll");
        if (m_hUpscalerDll)
        {
            m_pfnFsrDispatch = (void*)GetProcAddress(m_hUpscalerDll, "ffxFsrContextDispatch");
            m_pfnFsrCreate = (void*)GetProcAddress(m_hUpscalerDll, "ffxFsrContextCreate");
            m_pfnFsrDestroy = (void*)GetProcAddress(m_hUpscalerDll, "ffxFsrContextDestroy");
        }
    }

    if (!m_hFrameGenDll)
    {
        m_hFrameGenDll = LoadLibraryW(L"amd_fidelityfx_framegeneration_dx12.dll");
    }

    m_initialized = RCASBridge::Get().Initialize(pDevice);
    return m_initialized;
}

bool FSRBridge::DispatchUpscale(
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
    float sharpness)
{
    if (!pCmdList || !pInputColor || !pOutputColor)
    {
        return false;
    }

    if (!m_initialized)
    {
        ID3D12Device* pDevice = nullptr;
        if (SUCCEEDED(pCmdList->GetDevice(__uuidof(ID3D12Device), reinterpret_cast<void**>(&pDevice))))
        {
            Initialize(pDevice);
            pDevice->Release();
        }
    }

    const auto& config = AetherConfig::Get();

    if (config.pacing.targetFpsCap != g_lastTrackedCap)
    {
        g_lastTrackedCap = config.pacing.targetFpsCap;
        g_resetSignalFrames = 15; // Force FSR context pipeline reset for 15 frames on cap change
    }

    bool shouldResetContext = false;
    if (g_resetSignalFrames > 0)
    {
        shouldResetContext = true;
        g_resetSignalFrames--;
    }

    if (config.fsr.nativeAA)
    {
        if (displayWidth > 0) renderWidth = displayWidth;
        if (displayHeight > 0) renderHeight = displayHeight;
    }

    uint32_t targetWidth = displayWidth > 0 ? displayWidth : renderWidth;
    uint32_t targetHeight = displayHeight > 0 ? displayHeight : renderHeight;

    // Execute official FidelityFX dispatch if bound
    if (m_pfnFsrDispatch)
    {
        auto fnDispatch = reinterpret_cast<PFN_ffxFsrContextDispatch>(m_pfnFsrDispatch);
        struct FfxFsrDispatchDescription
        {
            ID3D12GraphicsCommandList* commandList;
            ID3D12Resource* color;
            ID3D12Resource* depth;
            ID3D12Resource* motionVectors;
            ID3D12Resource* exposure;
            ID3D12Resource* reactive;
            ID3D12Resource* transparencyAndComposition;
            ID3D12Resource* output;
            float jitterOffsetX;
            float jitterOffsetY;
            float motionVectorScaleX;
            float motionVectorScaleY;
            uint32_t renderSizeX;
            uint32_t renderSizeY;
            bool enableSharpening;
            float sharpness;
            float frameTimeDelta;
            float preExposure;
            bool reset;
            float cameraNear;
            float cameraFar;
            float cameraFovAngleVertical;
            float viewSpaceToMetersFactor;
            bool enableAutoReactive;
        };

        FfxFsrDispatchDescription dispatchDesc = {};
        dispatchDesc.commandList = pCmdList;
        dispatchDesc.color = pInputColor;
        dispatchDesc.depth = pDepth;
        dispatchDesc.motionVectors = pMotionVectors;
        dispatchDesc.output = pOutputColor;
        dispatchDesc.reactive = config.fsr.reactiveMask ? pInputColor : nullptr;
        dispatchDesc.jitterOffsetX = jitterX;
        dispatchDesc.jitterOffsetY = jitterY;
        dispatchDesc.renderSizeX = renderWidth;
        dispatchDesc.renderSizeY = renderHeight;
        dispatchDesc.enableSharpening = config.fsr.enableRCASOverride;
        dispatchDesc.sharpness = sharpness;
        dispatchDesc.reset = shouldResetContext;

        // Actually invoke the FidelityFX dispatch pipeline
        fnDispatch(nullptr, &dispatchDesc);
    }

    return RCASBridge::Get().DispatchRCAS(
        pCmdList,
        pInputColor,
        pOutputColor,
        targetWidth,
        targetHeight,
        sharpness
    );
}

void FSRBridge::Shutdown()
{
    if (m_pDevice)
    {
        m_pDevice->Release();
        m_pDevice = nullptr;
    }

    if (m_hUpscalerDll)
    {
        FreeLibrary(m_hUpscalerDll);
        m_hUpscalerDll = nullptr;
    }

    if (m_hFrameGenDll)
    {
        FreeLibrary(m_hFrameGenDll);
        m_hFrameGenDll = nullptr;
    }

    m_pfnFsrDispatch = nullptr;
    m_pfnFsrCreate = nullptr;
    m_pfnFsrDestroy = nullptr;
    m_initialized = false;
}