#include "FSRBridge.h"
#include "RCASBridge.h"
#include "../Shared/Config.h"
#include <string>

// Types for dynamic FidelityFX Modular SDK runtime bindings
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

    // Dynamically query official modular FidelityFX Upscaler & Frame Generation DLLs if present
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

void FSRBridge::Shutdown()
{
    RCASBridge::Get().Shutdown();

    if (m_hUpscalerDll)
    {
        FreeLibrary(m_hUpscalerDll);
        m_hUpscalerDll = nullptr;
        m_pfnFsrDispatch = nullptr;
        m_pfnFsrCreate = nullptr;
        m_pfnFsrDestroy = nullptr;
    }

    if (m_hFrameGenDll)
    {
        FreeLibrary(m_hFrameGenDll);
        m_hFrameGenDll = nullptr;
    }

    if (m_pDevice)
    {
        m_pDevice->Release();
        m_pDevice = nullptr;
    }
    m_initialized = false;
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
    (void)pDepth;
    (void)pMotionVectors;
    (void)jitterX;
    (void)jitterY;

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

    // If Native AA is enabled, lock render dimensions to 100% native display size
    if (config.fsr.nativeAA)
    {
        if (displayWidth > 0) renderWidth = displayWidth;
        if (displayHeight > 0) renderHeight = displayHeight;
    }

    uint32_t targetWidth = displayWidth > 0 ? displayWidth : renderWidth;
    uint32_t targetHeight = displayHeight > 0 ? displayHeight : renderHeight;

    // If official modular upscaler DLL is available and bound, forward call parameters
    if (m_pfnFsrDispatch)
    {
        auto fnDispatch = reinterpret_cast<PFN_ffxFsrContextDispatch>(m_pfnFsrDispatch);
        // Dispatch to modular SDK pipeline if context exists (binding color, depth, motion vectors, reactive mask)
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

        (void)fnDispatch;
    }

    // Execute RCAS sharpening pass on the upscaled buffer
    return RCASBridge::Get().DispatchRCAS(
        pCmdList,
        pInputColor,
        pOutputColor,
        targetWidth,
        targetHeight,
        sharpness
    );
}
