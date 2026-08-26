#include "RCASBridge.h"
#include <d3dcompiler.h>
#include <iostream>

RCASBridge& RCASBridge::Get()
{
    static RCASBridge instance;
    return instance;
}

bool RCASBridge::Initialize(ID3D12Device* pDevice)
{
    if (m_initialized && m_pDevice == pDevice) return true;
    if (m_initialized) Shutdown();

    if (!pDevice) return false;
    m_pDevice = pDevice;
    m_pDevice->AddRef();

    m_descriptorSize = m_pDevice->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV);

    if (!CompileShaderBytecode())
    {
        Shutdown();
        return false;
    }

    if (!CreateRootSignature())
    {
        Shutdown();
        return false;
    }

    if (!CreatePipelineState())
    {
        Shutdown();
        return false;
    }

    m_initialized = true;
    return true;
}

void RCASBridge::Shutdown()
{
    if (m_pPipelineState)
    {
        m_pPipelineState->Release();
        m_pPipelineState = nullptr;
    }

    if (m_pRootSignature)
    {
        m_pRootSignature->Release();
        m_pRootSignature = nullptr;
    }

    if (m_pCbvSrvUavHeap)
    {
        m_pCbvSrvUavHeap->Release();
        m_pCbvSrvUavHeap = nullptr;
    }

    if (m_pShaderBytecode)
    {
        free(m_pShaderBytecode);
        m_pShaderBytecode = nullptr;
        m_shaderBytecodeSize = 0;
    }

    if (m_pDevice)
    {
        m_pDevice->Release();
        m_pDevice = nullptr;
    }

    m_initialized = false;
}

bool RCASBridge::CompileShaderBytecode()
{
    const char rcasSource[] = R"(
        cbuffer RCASConstants : register(b0)
        {
            uint2 g_Resolution;
            float g_Sharpness;
            float g_Padding;
        };

        Texture2D<float4>   g_InputTexture   : register(t0);
        RWTexture2D<float4> g_OutputTexture  : register(u0);

        float RgbToLuma(float3 rgb)
        {
            return dot(rgb, float3(0.2126f, 0.7152f, 0.0722f));
        }

        [numthreads(8, 8, 1)]
        void CSMain(uint3 dispatchThreadId : SV_DispatchThreadID)
        {
            if (dispatchThreadId.x >= g_Resolution.x || dispatchThreadId.y >= g_Resolution.y)
                return;

            int2 pos = int2(dispatchThreadId.xy);

            float4 e = g_InputTexture.Load(int3(pos, 0));
            float4 b = g_InputTexture.Load(int3(clamp(pos + int2(0, -1), int2(0, 0), int2(g_Resolution) - 1), 0));
            float4 d = g_InputTexture.Load(int3(clamp(pos + int2(-1, 0), int2(0, 0), int2(g_Resolution) - 1), 0));
            float4 f = g_InputTexture.Load(int3(clamp(pos + int2(1, 0), int2(0, 0), int2(g_Resolution) - 1), 0));
            float4 h = g_InputTexture.Load(int3(clamp(pos + int2(0, 1), int2(0, 0), int2(g_Resolution) - 1), 0));

            float bL = RgbToLuma(b.rgb);
            float dL = RgbToLuma(d.rgb);
            float eL = RgbToLuma(e.rgb);
            float fL = RgbToLuma(f.rgb);
            float hL = RgbToLuma(h.rgb);

            float minL = min(eL, min(min(bL, dL), min(fL, hL)));
            float maxL = max(eL, max(max(bL, dL), max(fL, hL)));

            float contrast = max(maxL - minL, 0.001f);
            float amp = saturate(minL / contrast);
            float peak = -1.0f / lerp(8.0f, 5.0f, saturate(g_Sharpness));
            float w = amp * peak * g_Sharpness;

            float3 sharpenedRgb = (b.rgb * w + d.rgb * w + f.rgb * w + h.rgb * w + e.rgb) / (1.0f + 4.0f * w);

            float3 minRgb = min(e.rgb, min(min(b.rgb, d.rgb), min(f.rgb, h.rgb)));
            float3 maxRgb = max(e.rgb, max(max(b.rgb, d.rgb), max(f.rgb, h.rgb)));
            sharpenedRgb = clamp(sharpenedRgb, minRgb, maxRgb);

            g_OutputTexture[pos] = float4(sharpenedRgb, e.a);
        }
    )";

    ID3DBlob* pShaderBlob = nullptr;
    ID3DBlob* pErrorBlob = nullptr;

    HRESULT hr = D3DCompile(
        rcasSource,
        sizeof(rcasSource),
        "RCAS_CS",
        nullptr,
        nullptr,
        "CSMain",
        "cs_5_0",
        D3DCOMPILE_OPTIMIZATION_LEVEL3,
        0,
        &pShaderBlob,
        &pErrorBlob
    );

    if (FAILED(hr))
    {
        if (pErrorBlob)
        {
            pErrorBlob->Release();
        }
        return false;
    }

    m_shaderBytecodeSize = pShaderBlob->GetBufferSize();
    m_pShaderBytecode = malloc(m_shaderBytecodeSize);
    memcpy(m_pShaderBytecode, pShaderBlob->GetBufferPointer(), m_shaderBytecodeSize);

    pShaderBlob->Release();
    return true;
}

bool RCASBridge::CreateRootSignature()
{
    D3D12_ROOT_PARAMETER rootParams[3] = {};

    // 0: Root 32-bit Constants (b0)
    rootParams[0].ParameterType = D3D12_ROOT_PARAMETER_TYPE_32BIT_CONSTANTS;
    rootParams[0].Constants.ShaderRegister = 0;
    rootParams[0].Constants.RegisterSpace = 0;
    rootParams[0].Constants.Num32BitValues = sizeof(RCASConstants) / 4;
    rootParams[0].ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;

    // 1: SRV Descriptor Table (t0)
    D3D12_DESCRIPTOR_RANGE srvRange = {};
    srvRange.RangeType = D3D12_DESCRIPTOR_RANGE_TYPE_SRV;
    srvRange.NumDescriptors = 1;
    srvRange.BaseShaderRegister = 0;
    srvRange.RegisterSpace = 0;
    srvRange.OffsetInDescriptorsFromTableStart = D3D12_DESCRIPTOR_RANGE_OFFSET_APPEND;

    rootParams[1].ParameterType = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
    rootParams[1].DescriptorTable.NumDescriptorRanges = 1;
    rootParams[1].DescriptorTable.pDescriptorRanges = &srvRange;
    rootParams[1].ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;

    // 2: UAV Descriptor Table (u0)
    D3D12_DESCRIPTOR_RANGE uavRange = {};
    uavRange.RangeType = D3D12_DESCRIPTOR_RANGE_TYPE_UAV;
    uavRange.NumDescriptors = 1;
    uavRange.BaseShaderRegister = 0;
    uavRange.RegisterSpace = 0;
    uavRange.OffsetInDescriptorsFromTableStart = D3D12_DESCRIPTOR_RANGE_OFFSET_APPEND;

    rootParams[2].ParameterType = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
    rootParams[2].DescriptorTable.NumDescriptorRanges = 1;
    rootParams[2].DescriptorTable.pDescriptorRanges = &uavRange;
    rootParams[2].ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;

    D3D12_ROOT_SIGNATURE_DESC rootDesc = {};
    rootDesc.NumParameters = 3;
    rootDesc.pParameters = rootParams;
    rootDesc.NumStaticSamplers = 0;
    rootDesc.pStaticSamplers = nullptr;
    rootDesc.Flags = D3D12_ROOT_SIGNATURE_FLAG_NONE;

    ID3DBlob* pSignatureBlob = nullptr;
    ID3DBlob* pErrorBlob = nullptr;

    HRESULT hr = D3D12SerializeRootSignature(&rootDesc, D3D_ROOT_SIGNATURE_VERSION_1, &pSignatureBlob, &pErrorBlob);
    if (FAILED(hr))
    {
        if (pErrorBlob) pErrorBlob->Release();
        return false;
    }

    hr = m_pDevice->CreateRootSignature(0, pSignatureBlob->GetBufferPointer(), pSignatureBlob->GetBufferSize(), IID_PPV_ARGS(&m_pRootSignature));
    pSignatureBlob->Release();

    return SUCCEEDED(hr);
}

bool RCASBridge::CreatePipelineState()
{
    D3D12_COMPUTE_PIPELINE_STATE_DESC psoDesc = {};
    psoDesc.pRootSignature = m_pRootSignature;
    psoDesc.CS.pShaderBytecode = m_pShaderBytecode;
    psoDesc.CS.BytecodeLength = m_shaderBytecodeSize;
    psoDesc.Flags = D3D12_PIPELINE_STATE_FLAG_NONE;

    HRESULT hr = m_pDevice->CreateComputePipelineState(&psoDesc, IID_PPV_ARGS(&m_pPipelineState));
    return SUCCEEDED(hr);
}

bool RCASBridge::DispatchRCAS(
    ID3D12GraphicsCommandList* pCmdList,
    ID3D12Resource* pInputTexture,
    ID3D12Resource* pOutputTexture,
    uint32_t width,
    uint32_t height,
    float sharpness)
{
    if (!m_initialized || !pCmdList || !pInputTexture || !pOutputTexture)
        return false;

    if (!m_pCbvSrvUavHeap)
    {
        D3D12_DESCRIPTOR_HEAP_DESC heapDesc = {};
        heapDesc.NumDescriptors = 16;
        heapDesc.Type = D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV;
        heapDesc.Flags = D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE;

        HRESULT hr = m_pDevice->CreateDescriptorHeap(&heapDesc, IID_PPV_ARGS(&m_pCbvSrvUavHeap));
        if (FAILED(hr)) return false;
    }

    auto cpuHandle = m_pCbvSrvUavHeap->GetCPUDescriptorHandleForHeapStart();
    auto gpuHandle = m_pCbvSrvUavHeap->GetGPUDescriptorHandleForHeapStart();

    // Create SRV for input texture at slot 0
    m_pDevice->CreateShaderResourceView(pInputTexture, nullptr, cpuHandle);

    // Create UAV for output texture at slot 1
    D3D12_CPU_DESCRIPTOR_HANDLE uavCpu = { cpuHandle.ptr + m_descriptorSize };
    m_pDevice->CreateUnorderedAccessView(pOutputTexture, nullptr, nullptr, uavCpu);

    ID3D12DescriptorHeap* ppHeaps[] = { m_pCbvSrvUavHeap };
    pCmdList->SetDescriptorHeaps(1, ppHeaps);

    pCmdList->SetPipelineState(m_pPipelineState);
    pCmdList->SetComputeRootSignature(m_pRootSignature);

    RCASConstants constants;
    constants.resolution[0] = width;
    constants.resolution[1] = height;
    constants.sharpness = sharpness;
    constants.padding = 0.0f;

    pCmdList->SetComputeRoot32BitConstants(0, sizeof(RCASConstants) / 4, &constants, 0);

    // Set SRV table at param index 1
    pCmdList->SetComputeRootDescriptorTable(1, gpuHandle);

    // Set UAV table at param index 2
    D3D12_GPU_DESCRIPTOR_HANDLE uavGpu = { gpuHandle.ptr + m_descriptorSize };
    pCmdList->SetComputeRootDescriptorTable(2, uavGpu);

    uint32_t dispatchX = (width + 7) / 8;
    uint32_t dispatchY = (height + 7) / 8;

    pCmdList->Dispatch(dispatchX, dispatchY, 1);
    return true;
}
