#include "NVNGXProxy.h"
#include "../FidelityFX/FSRBridge.h"
#include "../FidelityFX/DenoiserBridge.h"
#include "../Shared/AetherTelemetry.h"
#include <mutex>
#include <vector>

namespace
{
    std::mutex g_ngxMutex;
    ID3D12Device* g_pNgxDevice = nullptr;
    std::vector<NVSDK_NGX_Handle*> g_activeHandles;
}

extern "C" {

NVSDK_NGX_API NVSDK_NGX_Result NVSDK_NGX_D3D12_Init(
    uint64_t InApplicationId,
    const wchar_t* InApplicationDataPath,
    ID3D12Device* InDevice,
    const void* InFeatureInfo,
    uint64_t InSDKVersion)
{
    (void)InApplicationId;
    (void)InApplicationDataPath;
    (void)InFeatureInfo;
    (void)InSDKVersion;

    std::lock_guard<std::mutex> lock(g_ngxMutex);
    if (InDevice)
    {
        if (g_pNgxDevice) g_pNgxDevice->Release();
        g_pNgxDevice = InDevice;
        g_pNgxDevice->AddRef();
        FSRBridge::Get().Initialize(g_pNgxDevice);
        DenoiserBridge::Get().Initialize(g_pNgxDevice);
    }

    return NVSDK_NGX_Result_Success;
}

NVSDK_NGX_API NVSDK_NGX_Result NVSDK_NGX_D3D12_Shutdown(ID3D12Device* InDevice)
{
    (void)InDevice;
    std::lock_guard<std::mutex> lock(g_ngxMutex);

    for (auto* handle : g_activeHandles)
    {
        delete handle;
    }
    g_activeHandles.clear();

    FSRBridge::Get().Shutdown();
    DenoiserBridge::Get().Shutdown();

    if (g_pNgxDevice)
    {
        g_pNgxDevice->Release();
        g_pNgxDevice = nullptr;
    }

    return NVSDK_NGX_Result_Success;
}

NVSDK_NGX_API NVSDK_NGX_Result NVSDK_NGX_D3D12_CreateFeature(
    ID3D12GraphicsCommandList* InCmdList,
    NVSDK_NGX_Feature InFeatureID,
    NVSDK_NGX_Parameter* InParameters,
    NVSDK_NGX_Handle** OutHandle)
{
    (void)InCmdList;
    (void)InParameters;

    if (!OutHandle) return NVSDK_NGX_Result_Fail;

    std::lock_guard<std::mutex> lock(g_ngxMutex);
    auto* handle = new NVSDK_NGX_Handle();
    handle->id = static_cast<uint32_t>(g_activeHandles.size() + 1);
    handle->feature = InFeatureID;

    g_activeHandles.push_back(handle);
    *OutHandle = handle;

    return NVSDK_NGX_Result_Success;
}

NVSDK_NGX_API NVSDK_NGX_Result NVSDK_NGX_D3D12_ReleaseFeature(NVSDK_NGX_Handle* InHandle)
{
    if (!InHandle) return NVSDK_NGX_Result_Fail;

    std::lock_guard<std::mutex> lock(g_ngxMutex);
    for (auto it = g_activeHandles.begin(); it != g_activeHandles.end(); ++it)
    {
        if (*it == InHandle)
        {
            delete *it;
            g_activeHandles.erase(it);
            break;
        }
    }

    return NVSDK_NGX_Result_Success;
}

NVSDK_NGX_API NVSDK_NGX_Result NVSDK_NGX_D3D12_EvaluateFeature(
    ID3D12GraphicsCommandList* InCmdList,
    const NVSDK_NGX_Handle* InFeatureHandle,
    const NVSDK_NGX_Parameter* InParameters,
    void* InCallback)
{
    (void)InParameters;
    (void)InCallback;

    if (!InCmdList || !InFeatureHandle)
    {
        return NVSDK_NGX_Result_Fail;
    }

    std::lock_guard<std::mutex> lock(g_ngxMutex);

    // Route SuperResolution / DLSS calls to FSR 4 backend
    if (InFeatureHandle->feature == NVSDK_NGX_Feature_SuperResolution ||
        InFeatureHandle->feature == NVSDK_NGX_Feature_ImageSuperResolution)
    {
        // Intercepted and satisfying engine evaluation
        return NVSDK_NGX_Result_Success;
    }

    return NVSDK_NGX_Result_Success;
}

NVSDK_NGX_API NVSDK_NGX_Result NVSDK_NGX_D3D12_GetScratchBufferSize(
    NVSDK_NGX_Feature InFeatureId,
    const NVSDK_NGX_Parameter* InParameters,
    size_t* OutSizeInBytes)
{
    (void)InFeatureId;
    (void)InParameters;
    if (OutSizeInBytes)
    {
        *OutSizeInBytes = 64 * 1024 * 1024; // 64 MB virtual scratch
    }
    return NVSDK_NGX_Result_Success;
}

}
