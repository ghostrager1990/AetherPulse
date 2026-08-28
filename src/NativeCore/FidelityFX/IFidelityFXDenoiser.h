#pragma once

#include <windows.h>
#include <d3d12.h>
#include <cstdint>

struct DenoiserResourceBundle
{
    ID3D12Resource* pDiffuseRadiance = nullptr;
    ID3D12Resource* pSpecularRadiance = nullptr;
    ID3D12Resource* pDepth = nullptr;
    ID3D12Resource* pNormals = nullptr;
    ID3D12Resource* pMotionVectors = nullptr;
    ID3D12Resource* pRoughness = nullptr;
    ID3D12Resource* pSpecularHitDist = nullptr;
    ID3D12Resource* pAlbedo = nullptr;
    ID3D12Resource* pOutputColor = nullptr;
};

class IFidelityFXDenoiser
{
public:
    virtual ~IFidelityFXDenoiser() = default;

    virtual bool Initialize(ID3D12Device* pDevice) = 0;
    virtual void Shutdown() = 0;

    virtual bool DispatchDenoiser(
        ID3D12GraphicsCommandList* pCmdList,
        const DenoiserResourceBundle& resources,
        uint32_t width,
        uint32_t height
    ) = 0;

    virtual bool IsInitialized() const = 0;
};
