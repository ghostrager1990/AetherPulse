#pragma once

#include <windows.h>
#include <d3d12.h>
#include <cstdint>

struct RCASConstants
{
    uint32_t resolution[2];
    float    sharpness;
    float    padding;
};

class RCASBridge
{
public:
    static RCASBridge& Get();

    bool Initialize(ID3D12Device* pDevice);
    void Shutdown();

    bool DispatchRCAS(
        ID3D12GraphicsCommandList* pCmdList,
        ID3D12Resource* pInputTexture,
        ID3D12Resource* pOutputTexture,
        uint32_t width,
        uint32_t height,
        float sharpness
    );

    bool IsInitialized() const { return m_initialized; }

private:
    RCASBridge() = default;
    ~RCASBridge() { Shutdown(); }

    RCASBridge(const RCASBridge&) = delete;
    RCASBridge& operator=(const RCASBridge&) = delete;

    bool CreateRootSignature();
    bool CreatePipelineState();
    bool CompileShaderBytecode();

    bool                 m_initialized = false;
    ID3D12Device*        m_pDevice = nullptr;
    ID3D12RootSignature* m_pRootSignature = nullptr;
    ID3D12PipelineState* m_pPipelineState = nullptr;

    ID3D12DescriptorHeap* m_pCbvSrvUavHeap = nullptr;
    UINT                  m_descriptorSize = 0;

    void*  m_pShaderBytecode = nullptr;
    size_t m_shaderBytecodeSize = 0;
};
