#include "StreamlineProxy.h"
#include "../FidelityFX/DenoiserBridge.h"
#include "../FidelityFX/FSRBridge.h"
#include "../Shared/AetherTelemetry.h"
#include "../Shared/Config.h"
#include <mutex>
#include <unordered_map>
#include <d3d12.h>

namespace
{
    std::mutex g_slMutex;
    bool g_slInitialized = false;
    ID3D12Device* g_pD3D12Device = nullptr;

    struct CachedTagResource
    {
        ID3D12Resource* pResource = nullptr;
        uint32_t        width = 0;
        uint32_t        height = 0;
        uint32_t        format = 0;
    };

    std::unordered_map<sl::BufferType, CachedTagResource> g_resourceCache;

    void ClearResourceCache()
    {
        for (auto& [type, res] : g_resourceCache)
        {
            if (res.pResource)
            {
                res.pResource->Release();
                res.pResource = nullptr;
            }
        }
        g_resourceCache.clear();
    }
}

namespace StreamlineProxy
{
    bool Initialize()
    {
        std::lock_guard<std::mutex> lock(g_slMutex);
        g_slInitialized = true;
        return true;
    }

    void Shutdown()
    {
        std::lock_guard<std::mutex> lock(g_slMutex);
        ClearResourceCache();
        DenoiserBridge::Get().Shutdown();

        if (g_pD3D12Device)
        {
            g_pD3D12Device->Release();
            g_pD3D12Device = nullptr;
        }

        g_slInitialized = false;
    }
}

extern "C" {

SL_EXPORT sl::Result slInit(const sl::Preferences* pref, uint64_t sdkVersion)
{
    std::lock_guard<std::mutex> lock(g_slMutex);
    g_slInitialized = true;

    if (pref && pref->renderDevice)
    {
        IUnknown* pUnk = static_cast<IUnknown*>(pref->renderDevice);
        ID3D12Device* pDev = nullptr;
        if (SUCCEEDED(pUnk->QueryInterface(__uuidof(ID3D12Device), reinterpret_cast<void**>(&pDev))))
        {
            if (g_pD3D12Device) g_pD3D12Device->Release();
            g_pD3D12Device = pDev;
            DenoiserBridge::Get().Initialize(g_pD3D12Device);
        }
    }

    return sl::Result::eOk;
}

SL_EXPORT sl::Result slShutdown()
{
    StreamlineProxy::Shutdown();
    return sl::Result::eOk;
}

SL_EXPORT sl::Result slIsFeatureSupported(sl::Feature feature, const sl::Preferences* pref)
{
    // Claim support for DLSS-D (Ray Reconstruction / Denoiser), DLSS, and NRD
    if (feature == sl::kFeatureDLSS_D || feature == sl::kFeatureDLSS || feature == sl::kFeatureNRD)
    {
        return sl::Result::eOk;
    }

    return sl::Result::eOk;
}

SL_EXPORT sl::Result slGetFeatureRequirements(sl::Feature feature, sl::FeatureRequirements* requirements)
{
    if (requirements)
    {
        requirements->flags = 0;
        requirements->maxSupportedArchitecture = 0xFFFFFFFF;
        requirements->minOSVersionBuild = 0;
        requirements->minDriverVersionMajor = 0;
        requirements->minDriverVersionMinor = 0;
    }
    return sl::Result::eOk;
}

SL_EXPORT sl::Result slIsFeatureLoaded(sl::Feature feature, bool& loaded)
{
    loaded = (feature == sl::kFeatureDLSS_D || feature == sl::kFeatureDLSS);
    return sl::Result::eOk;
}

SL_EXPORT sl::Result slSetFeatureLoaded(sl::Feature feature, bool loaded)
{
    return sl::Result::eOk;
}

SL_EXPORT sl::Result slSetTag(
    const sl::ViewportHandle* viewport,
    const sl::ResourceTag* tags,
    uint32_t numTags,
    void* cmdList)
{
    if (!tags || numTags == 0) return sl::Result::eOk;

    std::lock_guard<std::mutex> lock(g_slMutex);

    for (uint32_t i = 0; i < numTags; ++i)
    {
        const sl::ResourceTag& tag = tags[i];
        if (!tag.resource || !tag.resource->nativeResource)
        {
            continue;
        }

        ID3D12Resource* pRes = static_cast<ID3D12Resource*>(tag.resource->nativeResource);
        pRes->AddRef();

        auto it = g_resourceCache.find(tag.type);
        if (it != g_resourceCache.end() && it->second.pResource)
        {
            it->second.pResource->Release();
        }

        CachedTagResource cached;
        cached.pResource = pRes;
        cached.width = tag.resource->width;
        cached.height = tag.resource->height;
        cached.format = tag.resource->nativeFormat;

        g_resourceCache[tag.type] = cached;

        // If D3D12 device is not yet initialized, acquire it from the resource
        if (!g_pD3D12Device)
        {
            if (SUCCEEDED(pRes->GetDevice(__uuidof(ID3D12Device), reinterpret_cast<void**>(&g_pD3D12Device))))
            {
                DenoiserBridge::Get().Initialize(g_pD3D12Device);
            }
        }
    }

    return sl::Result::eOk;
}

SL_EXPORT sl::Result slEvaluateFeature(
    sl::Feature feature,
    const sl::FrameToken* frame,
    const void* const* tags,
    uint32_t numTags,
    void* cmdList)
{
    if (feature != sl::kFeatureDLSS_D && feature != sl::kFeatureDLSS)
    {
        return sl::Result::eOk;
    }

    if (!cmdList)
    {
        return sl::Result::eErrorInvalidParameter;
    }

    ID3D12GraphicsCommandList* pCmdList = static_cast<ID3D12GraphicsCommandList*>(cmdList);

    std::lock_guard<std::mutex> lock(g_slMutex);

    if (!g_pD3D12Device)
    {
        if (SUCCEEDED(pCmdList->GetDevice(__uuidof(ID3D12Device), reinterpret_cast<void**>(&g_pD3D12Device))))
        {
            DenoiserBridge::Get().Initialize(g_pD3D12Device);
            FSRBridge::Get().Initialize(g_pD3D12Device);
        }
    }

    auto getResource = [](sl::BufferType type) -> ID3D12Resource* {
        auto it = g_resourceCache.find(type);
        return (it != g_resourceCache.end()) ? it->second.pResource : nullptr;
    };

    if (feature == sl::kFeatureDLSS)
    {
        ID3D12Resource* pInput = getResource(sl::kBufferTypeScalingInputColor);
        if (!pInput) pInput = getResource(sl::kBufferTypeHUDLessColor);

        ID3D12Resource* pOutput = getResource(sl::kBufferTypeScalingOutputColor);
        ID3D12Resource* pDepth  = getResource(sl::kBufferTypeDepth);
        ID3D12Resource* pMv     = getResource(sl::kBufferTypeMotionVectors);

        uint32_t inW = 1920, inH = 1080, outW = 1920, outH = 1080;
        auto itIn = g_resourceCache.find(sl::kBufferTypeScalingInputColor);
        if (itIn != g_resourceCache.end() && itIn->second.width > 0)
        {
            inW = itIn->second.width;
            inH = itIn->second.height;
        }

        auto itOut = g_resourceCache.find(sl::kBufferTypeScalingOutputColor);
        if (itOut != g_resourceCache.end() && itOut->second.width > 0)
        {
            outW = itOut->second.width;
            outH = itOut->second.height;
        }

        if (pInput && pOutput)
        {
            FSRBridge::Get().DispatchUpscale(pCmdList, pInput, pOutput, pDepth, pMv, inW, inH, outW, outH, 0.0f, 0.0f, 0.5f);
        }

        return sl::Result::eOk;
    }

    DenoiserResourceBundle bundle = {};
    uint32_t width = 0;
    uint32_t height = 0;

    bundle.pDiffuseRadiance  = getResource(sl::kBufferTypeDiffuseRadiance);
    bundle.pSpecularRadiance = getResource(sl::kBufferTypeSpecularRadiance);
    bundle.pDepth            = getResource(sl::kBufferTypeDepth);
    bundle.pNormals          = getResource(sl::kBufferTypeNormals);
    bundle.pMotionVectors    = getResource(sl::kBufferTypeMotionVectors);
    bundle.pRoughness        = getResource(sl::kBufferTypeRoughness);
    bundle.pSpecularHitDist  = getResource(sl::kBufferTypeSpecularHitDistance);
    bundle.pAlbedo           = getResource(sl::kBufferTypeAlbedo);
    bundle.pOutputColor      = getResource(sl::kBufferTypeScalingOutputColor);

    if (!bundle.pOutputColor)
    {
        bundle.pOutputColor = getResource(sl::kBufferTypeHUDLessColor);
    }

    auto itSpec = g_resourceCache.find(sl::kBufferTypeSpecularRadiance);
    if (itSpec != g_resourceCache.end())
    {
        width = itSpec->second.width;
        height = itSpec->second.height;
    }
    else
    {
        auto itDiff = g_resourceCache.find(sl::kBufferTypeDiffuseRadiance);
        if (itDiff != g_resourceCache.end())
        {
            width = itDiff->second.width;
            height = itDiff->second.height;
        }
    }

    if (width > 0 && height > 0)
    {
        DenoiserBridge::Get().DispatchDenoiser(pCmdList, bundle, width, height);
    }

    return sl::Result::eOk;
}

SL_EXPORT sl::Result slAllocateResources(void* cmdList, sl::Feature feature, const sl::ViewportHandle* viewport)
{
    return sl::Result::eOk;
}

SL_EXPORT sl::Result slFreeResources(sl::Feature feature, const sl::ViewportHandle* viewport)
{
    return sl::Result::eOk;
}

} // extern "C"
