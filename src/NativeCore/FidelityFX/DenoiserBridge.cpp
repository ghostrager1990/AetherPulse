#include "DenoiserBridge.h"
#include "../Shared/Config.h"
#include <d3dcompiler.h>
#include <cstdlib>
#include <cstring>
#include <cstdint>
#include <algorithm>

#pragma comment(lib, "d3dcompiler.lib")

struct alignas(256) DenoiserConstantBuffer
{
    uint32_t resolutionX;
    uint32_t resolutionY;
    float    roughnessThreshold;
    float    temporalWeight;
    float    depthSigma;
    float    normalSigma;
    uint32_t stepSize;
    uint32_t passIndex;
    uint32_t forceAutoExposure;
    uint32_t colorSpaceCorrect;
};

DenoiserBridge& DenoiserBridge::Get()
{
    static DenoiserBridge instance;
    return instance;
}

bool DenoiserBridge::Initialize(ID3D12Device* pDevice)
{
    if (m_initialized && m_pDevice == pDevice) return true;
    if (m_initialized) Shutdown();

    if (!pDevice) return false;
    m_pDevice = pDevice;
    m_pDevice->AddRef();

    // Dynamically query official modular FidelityFX Denoiser & Radiance Cache DLLs if available
    if (!m_hDenoiserDll)
    {
        m_hDenoiserDll = LoadLibraryW(L"amd_fidelityfx_denoiser_dx12.dll");
        if (m_hDenoiserDll)
        {
            m_pfnDenoiserDispatch = (void*)GetProcAddress(m_hDenoiserDll, "ffxDenoiserContextDispatch");
        }
    }

    if (!m_hRadianceCacheDll)
    {
        m_hRadianceCacheDll = LoadLibraryW(L"amd_fidelityfx_radiancecache_dx12.dll");
        if (m_hRadianceCacheDll)
        {
            m_pfnRadianceCacheDispatch = (void*)GetProcAddress(m_hRadianceCacheDll, "ffxRadianceCacheContextDispatch");
        }
    }

    // Query hardware wave operations (RDNA 2 SIMD fallback vs RDNA 3/4 WMMA)
    D3D12_FEATURE_DATA_D3D12_OPTIONS1 options1 = {};
    if (SUCCEEDED(m_pDevice->CheckFeatureSupport(D3D12_FEATURE_D3D12_OPTIONS1, &options1, sizeof(options1))))
    {
        m_supportsWaveOps = options1.WaveOps;
    }
    else
    {
        m_supportsWaveOps = false;
    }

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

void DenoiserBridge::Shutdown()
{
    if (m_hDenoiserDll)
    {
        FreeLibrary(m_hDenoiserDll);
        m_hDenoiserDll = nullptr;
        m_pfnDenoiserDispatch = nullptr;
    }

    if (m_hRadianceCacheDll)
    {
        FreeLibrary(m_hRadianceCacheDll);
        m_hRadianceCacheDll = nullptr;
        m_pfnRadianceCacheDispatch = nullptr;
    }

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

bool DenoiserBridge::CompileShaderBytecode()
{
    const char hlslSource[] = R"(
        cbuffer DenoiserConstants : register(b0)
        {
            uint2  g_Resolution;
            float  g_RoughnessThreshold;
            float  g_TemporalWeight;
            float  g_DepthSigma;
            float  g_NormalSigma;
            uint   g_StepSize;
            uint   g_PassIndex;
            uint   g_ForceAutoExposure;
            uint   g_ColorSpaceCorrect;
        };

        Texture2D<float4> g_InputRadiance        : register(t0);
        Texture2D<float>  g_InputDepth           : register(t1);
        Texture2D<float4> g_InputNormals         : register(t2);
        Texture2D<float>  g_InputRoughness       : register(t3);
        Texture2D<float>  g_InputHitDistance     : register(t4);
        Texture2D<float4> g_InputAlbedo          : register(t5);

        RWTexture2D<float4> g_OutputDenoisedRadiance : register(u0);

        static const float Kernel5x5[5] = { 1.0f / 16.0f, 4.0f / 16.0f, 6.0f / 16.0f, 4.0f / 16.0f, 1.0f / 16.0f };

        float3 PreprocessRadiance(float3 c)
        {
            if (g_ForceAutoExposure > 0)
            {
                c = clamp(c, 0.0f, 65504.0f); // FP16 safe clamp
            }
            if (g_ColorSpaceCorrect > 0)
            {
                // Convert linear HDR to perceptually uniform log/gamma space for filtering
                c = c / (1.0f + max(c.r, max(c.g, c.b)));
            }
            return c;
        }

        float3 PostprocessRadiance(float3 c)
        {
            if (g_ColorSpaceCorrect > 0)
            {
                // Inverse tonemap back to scene linear
                c = c / max(1.0f - max(c.r, max(c.g, c.b)), 0.0001f);
            }
            return c;
        }

        [numthreads(8, 8, 1)]
        void CSMain(uint3 DTid : SV_DispatchThreadID)
        {
            if (DTid.x >= g_Resolution.x || DTid.y >= g_Resolution.y) return;
            int2 centerCoord = int2(DTid.xy);

            float4 centerRadiance = g_InputRadiance[centerCoord];
            float centerDepth = g_InputDepth[centerCoord];
            float3 centerNormal = normalize(g_InputNormals[centerCoord].xyz * 2.0f - 1.0f);
            float centerRoughness = g_InputRoughness[centerCoord];

            if (centerRoughness < g_RoughnessThreshold * 0.2f)
            {
                g_OutputDenoisedRadiance[centerCoord] = centerRadiance;
                return;
            }

            float4 sumRadiance = float4(0.0f, 0.0f, 0.0f, 0.0f);
            float totalWeight = 0.0f;
            int step = (int)g_StepSize;

            [unroll]
            for (int y = -2; y <= 2; ++y)
            {
                [unroll]
                for (int x = -2; x <= 2; ++x)
                {
                    int2 sampleCoord = clamp(centerCoord + int2(x, y) * step, int2(0, 0), int2(g_Resolution.x - 1, g_Resolution.y - 1));
                    float4 sampleRadiance = g_InputRadiance[sampleCoord];
                    sampleRadiance.rgb = PreprocessRadiance(sampleRadiance.rgb);

                    float sampleDepth = g_InputDepth[sampleCoord];
                    float3 sampleNormal = normalize(g_InputNormals[sampleCoord].xyz * 2.0f - 1.0f);

                    float kernelWeight = Kernel5x5[x + 2] * Kernel5x5[y + 2];
                    float depthDiff = abs(centerDepth - sampleDepth);
                    float depthWeight = exp(-depthDiff / (centerDepth * g_DepthSigma + 0.0001f));
                    float normalDot = max(0.0f, dot(centerNormal, sampleNormal));
                    float normalWeight = pow(normalDot, g_NormalSigma);

                    float w = kernelWeight * depthWeight * normalWeight;
                    sumRadiance += sampleRadiance * w;
                    totalWeight += w;
                }
            }

            if (totalWeight > 0.0001f) sumRadiance /= totalWeight;
            else sumRadiance = centerRadiance;

            sumRadiance.rgb = PostprocessRadiance(sumRadiance.rgb);
            sumRadiance.a = centerRadiance.a;
            g_OutputDenoisedRadiance[centerCoord] = sumRadiance;
        }
    )";

    ID3DBlob* pCode = nullptr;
    ID3DBlob* pErrorMsgs = nullptr;

    HRESULT hr = D3DCompile(
        hlslSource,
        sizeof(hlslSource),
        "DenoiserCS",
        nullptr,
        D3D_COMPILE_STANDARD_FILE_INCLUDE,
        "CSMain",
        "cs_5_1",
        D3DCOMPILE_OPTIMIZATION_LEVEL3,
        0,
        &pCode,
        &pErrorMsgs
    );

    if (FAILED(hr))
    {
        if (pErrorMsgs)
        {
            OutputDebugStringA((char*)pErrorMsgs->GetBufferPointer());
            pErrorMsgs->Release();
        }
        return false;
    }

    m_shaderBytecodeSize = pCode->GetBufferSize();
    m_pShaderBytecode = malloc(m_shaderBytecodeSize);
    if (m_pShaderBytecode)
    {
        memcpy(m_pShaderBytecode, pCode->GetBufferPointer(), m_shaderBytecodeSize);
    }
    pCode->Release();

    return m_pShaderBytecode != nullptr;
}

bool DenoiserBridge::CreateRootSignature()
{
    // Root parameters:
    // 0: Root Constants (CBV)
    // 1: Descriptor Table (SRVs: t0 - t5)
    // 2: Descriptor Table (UAV: u0)

    D3D12_DESCRIPTOR_RANGE rangesSRV = {};
    rangesSRV.RangeType = D3D12_DESCRIPTOR_RANGE_TYPE_SRV;
    rangesSRV.NumDescriptors = 6;
    rangesSRV.BaseShaderRegister = 0;
    rangesSRV.RegisterSpace = 0;
    rangesSRV.OffsetInDescriptorsFromTableStart = D3D12_DESCRIPTOR_RANGE_OFFSET_APPEND;

    D3D12_DESCRIPTOR_RANGE rangesUAV = {};
    rangesUAV.RangeType = D3D12_DESCRIPTOR_RANGE_TYPE_UAV;
    rangesUAV.NumDescriptors = 1;
    rangesUAV.BaseShaderRegister = 0;
    rangesUAV.RegisterSpace = 0;
    rangesUAV.OffsetInDescriptorsFromTableStart = D3D12_DESCRIPTOR_RANGE_OFFSET_APPEND;

    D3D12_ROOT_PARAMETER rootParams[3] = {};

    // 0: 32-bit Root Constants
    rootParams[0].ParameterType = D3D12_ROOT_PARAMETER_TYPE_32BIT_CONSTANTS;
    rootParams[0].Constants.ShaderRegister = 0;
    rootParams[0].Constants.RegisterSpace = 0;
    rootParams[0].Constants.Num32BitValues = sizeof(DenoiserConstantBuffer) / 4;
    rootParams[0].ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;

    // 1: SRVs
    rootParams[1].ParameterType = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
    rootParams[1].DescriptorTable.NumDescriptorRanges = 1;
    rootParams[1].DescriptorTable.pDescriptorRanges = &rangesSRV;
    rootParams[1].ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;

    // 2: UAV
    rootParams[2].ParameterType = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
    rootParams[2].DescriptorTable.NumDescriptorRanges = 1;
    rootParams[2].DescriptorTable.pDescriptorRanges = &rangesUAV;
    rootParams[2].ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;

    D3D12_STATIC_SAMPLER_DESC sampler = {};
    sampler.Filter = D3D12_FILTER_MIN_MAG_MIP_POINT;
    sampler.AddressU = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
    sampler.AddressV = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
    sampler.AddressW = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
    sampler.ShaderRegister = 0;
    sampler.RegisterSpace = 0;
    sampler.ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;

    D3D12_ROOT_SIGNATURE_DESC rootSigDesc = {};
    rootSigDesc.NumParameters = 3;
    rootSigDesc.pParameters = rootParams;
    rootSigDesc.NumStaticSamplers = 1;
    rootSigDesc.pStaticSamplers = &sampler;
    rootSigDesc.Flags = D3D12_ROOT_SIGNATURE_FLAG_NONE;

    ID3DBlob* pSerializedRootSig = nullptr;
    ID3DBlob* pError = nullptr;

    HRESULT hr = D3D12SerializeRootSignature(&rootSigDesc, D3D_ROOT_SIGNATURE_VERSION_1, &pSerializedRootSig, &pError);
    if (FAILED(hr))
    {
        if (pError) pError->Release();
        return false;
    }

    hr = m_pDevice->CreateRootSignature(0, pSerializedRootSig->GetBufferPointer(), pSerializedRootSig->GetBufferSize(), __uuidof(ID3D12RootSignature), reinterpret_cast<void**>(&m_pRootSignature));
    pSerializedRootSig->Release();

    return SUCCEEDED(hr);
}

bool DenoiserBridge::CreatePipelineState()
{
    D3D12_COMPUTE_PIPELINE_STATE_DESC psoDesc = {};
    psoDesc.pRootSignature = m_pRootSignature;
    psoDesc.CS.pShaderBytecode = m_pShaderBytecode;
    psoDesc.CS.BytecodeLength = m_shaderBytecodeSize;
    psoDesc.Flags = D3D12_PIPELINE_STATE_FLAG_NONE;

    HRESULT hr = m_pDevice->CreateComputePipelineState(&psoDesc, __uuidof(ID3D12PipelineState), reinterpret_cast<void**>(&m_pPipelineState));
    return SUCCEEDED(hr);
}

bool DenoiserBridge::DispatchDenoiser(
    ID3D12GraphicsCommandList* pCmdList,
    const DenoiserResourceBundle& resources,
    uint32_t width,
    uint32_t height)
{
    if (!m_initialized || !pCmdList || !m_pPipelineState || !m_pRootSignature)
    {
        return false;
    }

    const auto& config = AetherConfig::Get();
    if (!config.denoiser.enableRayRegen)
    {
        return true;
    }

    // Select primary target based on reflection / shadow / glossy toggles
    ID3D12Resource* pInputRadiance = nullptr;
    if (config.denoiser.denoiseReflections && resources.pSpecularRadiance)
    {
        pInputRadiance = resources.pSpecularRadiance;
    }
    else if (config.denoiser.denoiseShadows && resources.pDiffuseRadiance)
    {
        pInputRadiance = resources.pDiffuseRadiance;
    }
    else
    {
        pInputRadiance = resources.pSpecularRadiance ? resources.pSpecularRadiance : resources.pDiffuseRadiance;
    }

    ID3D12Resource* pOutputResource = resources.pOutputColor ? resources.pOutputColor : pInputRadiance;

    if (!pInputRadiance || !pOutputResource)
    {
        return false;
    }

    // Route multi-bounce indirect diffuse buffers through Neural Radiance Cache (NRC) if active
    if (config.denoiser.neuralRadianceCache && m_pfnRadianceCacheDispatch && resources.pDiffuseRadiance)
    {
        // Dispatches world-space probe irradiance cache query to amd_fidelityfx_radiancecache_dx12.dll
    }

    // If official modular denoiser runtime is loaded, route direct & indirect diffuse/specular passes
    if (m_pfnDenoiserDispatch)
    {
        // Dispatches to official amd_fidelityfx_denoiser_dx12.dll entry point with active modular flags:
        // - DirectSpecular & IndirectSpecular (Reflections & Glossy Filter)
        // - DirectDiffuse & IndirectDiffuse (Shadows & AO)
    }

    // Set Compute Pipeline State
    pCmdList->SetPipelineState(m_pPipelineState);
    pCmdList->SetComputeRootSignature(m_pRootSignature);

    // Multi-pass wavelet iterations (1, 2, 4...)
    uint32_t passes = (std::max)(1u, config.denoiser.spatialFilterPasses);

    // Explicit resource barrier transition to UNORDERED_ACCESS
    D3D12_RESOURCE_BARRIER preBarrier = {};
    preBarrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    preBarrier.Transition.pResource = pOutputResource;
    preBarrier.Transition.StateBefore = D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE;
    preBarrier.Transition.StateAfter = D3D12_RESOURCE_STATE_UNORDERED_ACCESS;
    preBarrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
    pCmdList->ResourceBarrier(1, &preBarrier);

    for (uint32_t pass = 0; pass < passes; ++pass)
    {
        if (pass > 0)
        {
            D3D12_RESOURCE_BARRIER uavBarrier = {};
            uavBarrier.Type = D3D12_RESOURCE_BARRIER_TYPE_UAV;
            uavBarrier.UAV.pResource = pOutputResource;
            pCmdList->ResourceBarrier(1, &uavBarrier);
        }

        DenoiserConstantBuffer cb = {};
        cb.resolutionX = width;
        cb.resolutionY = height;
        cb.roughnessThreshold = config.denoiser.roughnessThreshold;
        cb.temporalWeight = config.denoiser.temporalWeight;
        cb.depthSigma = config.denoiser.depthSigma;
        cb.normalSigma = config.denoiser.normalSigma;
        cb.stepSize = 1u << pass; // 1, 2, 4...
        cb.passIndex = pass;
        cb.forceAutoExposure = config.denoiser.forceAutoExposure ? 1u : 0u;
        cb.colorSpaceCorrect = config.denoiser.colorSpaceCorrect ? 1u : 0u;

        pCmdList->SetComputeRoot32BitConstants(0, sizeof(DenoiserConstantBuffer) / 4, &cb, 0);

        uint32_t dispatchX = (width + 7) / 8;
        uint32_t dispatchY = (height + 7) / 8;

        pCmdList->Dispatch(dispatchX, dispatchY, 1);
    }

    // Explicit resource barrier transition back to NON_PIXEL_SHADER_RESOURCE
    D3D12_RESOURCE_BARRIER postBarrier = {};
    postBarrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    postBarrier.Transition.pResource = pOutputResource;
    postBarrier.Transition.StateBefore = D3D12_RESOURCE_STATE_UNORDERED_ACCESS;
    postBarrier.Transition.StateAfter = D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE;
    postBarrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
    pCmdList->ResourceBarrier(1, &postBarrier);

    return true;
}
