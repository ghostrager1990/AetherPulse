#pragma once

#include <windows.h>
#include <d3d12.h>
#include <cstdint>

#define NVSDK_NGX_API __declspec(dllexport)

enum NVSDK_NGX_Feature
{
    NVSDK_NGX_Feature_SuperResolution = 1,
    NVSDK_NGX_Feature_RayReconstruction = 2,
    NVSDK_NGX_Feature_DeepResolve = 3,
    NVSDK_NGX_Feature_ImageSuperResolution = 4
};

enum NVSDK_NGX_Result
{
    NVSDK_NGX_Result_Success = 0,
    NVSDK_NGX_Result_Fail = 1
};

struct NVSDK_NGX_Parameter
{
    void* handle;
};

struct NVSDK_NGX_Handle
{
    uint32_t id;
    NVSDK_NGX_Feature feature;
};

extern "C" {

NVSDK_NGX_API NVSDK_NGX_Result NVSDK_NGX_D3D12_Init(
    uint64_t InApplicationId,
    const wchar_t* InApplicationDataPath,
    ID3D12Device* InDevice,
    const void* InFeatureInfo = nullptr,
    uint64_t InSDKVersion = 0x000013
);

NVSDK_NGX_API NVSDK_NGX_Result NVSDK_NGX_D3D12_Shutdown(ID3D12Device* InDevice = nullptr);

NVSDK_NGX_API NVSDK_NGX_Result NVSDK_NGX_D3D12_CreateFeature(
    ID3D12GraphicsCommandList* InCmdList,
    NVSDK_NGX_Feature InFeatureID,
    NVSDK_NGX_Parameter* InParameters,
    NVSDK_NGX_Handle** OutHandle
);

NVSDK_NGX_API NVSDK_NGX_Result NVSDK_NGX_D3D12_ReleaseFeature(NVSDK_NGX_Handle* InHandle);

NVSDK_NGX_API NVSDK_NGX_Result NVSDK_NGX_D3D12_EvaluateFeature(
    ID3D12GraphicsCommandList* InCmdList,
    const NVSDK_NGX_Handle* InFeatureHandle,
    const NVSDK_NGX_Parameter* InParameters,
    void* InCallback = nullptr
);

NVSDK_NGX_API NVSDK_NGX_Result NVSDK_NGX_D3D12_GetScratchBufferSize(
    NVSDK_NGX_Feature InFeatureId,
    const NVSDK_NGX_Parameter* InParameters,
    size_t* OutSizeInBytes
);

}
