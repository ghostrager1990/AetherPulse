#pragma once

#include <windows.h>
#include <d3d12.h>
#include <cstdint>
#include <unordered_map>
#include "IFidelityFXDenoiser.h"
#include "../Streamline/StreamlineTags.h"

class DenoiserBridge : public IFidelityFXDenoiser
{
public:
    static DenoiserBridge& Get();

    bool Initialize(ID3D12Device* pDevice) override;
    void Shutdown() override;

    bool DispatchDenoiser(
        ID3D12GraphicsCommandList* pCmdList,
        const DenoiserResourceBundle& resources,
        uint32_t width,
        uint32_t height
    ) override;

    bool IsInitialized() const override { return m_initialized; }

private:
    DenoiserBridge() = default;
    ~DenoiserBridge() { Shutdown(); }

    DenoiserBridge(const DenoiserBridge&) = delete;
    DenoiserBridge& operator=(const DenoiserBridge&) = delete;

    bool CreateRootSignature();
    bool CreatePipelineState();
    bool CompileShaderBytecode();

    bool                    m_initialized = false;
    ID3D12Device*           m_pDevice = nullptr;
    ID3D12RootSignature*    m_pRootSignature = nullptr;
    ID3D12PipelineState*    m_pPipelineState = nullptr;

    ID3D12DescriptorHeap*   m_pCbvSrvUavHeap = nullptr;
    UINT                    m_descriptorSize = 0;

    void*                   m_pShaderBytecode = nullptr;
    size_t                  m_shaderBytecodeSize = 0;
    bool                    m_supportsWaveOps = false;

    HMODULE                 m_hDenoiserDll = nullptr;
    void*                   m_pfnDenoiserDispatch = nullptr;

    HMODULE                 m_hRadianceCacheDll = nullptr;
    void*                   m_pfnRadianceCacheDispatch = nullptr;
};
