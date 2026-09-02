#include <windows.h>
#include <dxgi1_6.h>
#include <d3d12.h>
#include <d3dcompiler.h>
#include <emmintrin.h>
#include <timeapi.h>
#include <atomic>
#include <cstdint>
#include <cstdio>
#include <cmath>
#include <algorithm>
#include "../Shared/AetherPulseShared.h"

#pragma comment(lib, "winmm.lib")
#pragma comment(lib, "d3dcompiler.lib")

static void LogDXGI(const char* fmt, ...)
{
    char buf[512];
    va_list args;
    va_start(args, fmt);
    vsnprintf(buf, sizeof(buf), fmt, args);
    va_end(args);
    OutputDebugStringA(buf);
}

typedef HRESULT(WINAPI* PFN_CreateDXGIFactory)(REFIID riid, void** ppFactory);
typedef HRESULT(WINAPI* PFN_CreateDXGIFactory1)(REFIID riid, void** ppFactory);
typedef HRESULT(WINAPI* PFN_CreateDXGIFactory2)(UINT Flags, REFIID riid, void** ppFactory);

typedef HRESULT(WINAPI* PFN_Present)(IDXGISwapChain* This, UINT SyncInterval, UINT Flags);
typedef HRESULT(WINAPI* PFN_Present1)(IDXGISwapChain1* This, UINT SyncInterval, UINT Flags, const DXGI_PRESENT_PARAMETERS* pPresentParameters);
typedef HRESULT(WINAPI* PFN_CreateSwapChain)(IDXGIFactory* This, IUnknown* pDevice, DXGI_SWAP_CHAIN_DESC* pDesc, IDXGISwapChain** ppSwapChain);
typedef HRESULT(WINAPI* PFN_CreateSwapChainForHwnd)(IDXGIFactory2* This, IUnknown* pDevice, HWND hWnd, const DXGI_SWAP_CHAIN_DESC1* pDesc, const DXGI_SWAP_CHAIN_FULLSCREEN_DESC* pFullscreenDesc, IDXGIOutput* pRestrictToOutput, IDXGISwapChain1** ppSwapChain);
typedef HRESULT(WINAPI* PFN_CreateSwapChainForCoreWindow)(IDXGIFactory2* This, IUnknown* pDevice, IUnknown* pWindow, const DXGI_SWAP_CHAIN_DESC1* pDesc, IDXGIOutput* pRestrictToOutput, IDXGISwapChain1** ppSwapChain);
typedef HRESULT(WINAPI* PFN_CreateSwapChainForComposition)(IDXGIFactory2* This, IUnknown* pDevice, const DXGI_SWAP_CHAIN_DESC1* pDesc, IDXGIOutput* pRestrictToOutput, IDXGISwapChain1** ppSwapChain);

static HMODULE g_hSystemDxgi = NULL;
static PFN_CreateDXGIFactory  g_pfnSysCreateDXGIFactory = nullptr;
static PFN_CreateDXGIFactory1 g_pfnSysCreateDXGIFactory1 = nullptr;
static PFN_CreateDXGIFactory2 g_pfnSysCreateDXGIFactory2 = nullptr;

static PFN_Present  g_pfnOriginalPresent = nullptr;
static PFN_Present1 g_pfnOriginalPresent1 = nullptr;
static PFN_CreateSwapChain g_pfnOriginalCreateSwapChain = nullptr;
static PFN_CreateSwapChainForHwnd g_pfnOriginalCreateSwapChainForHwnd = nullptr;
static PFN_CreateSwapChainForCoreWindow g_pfnOriginalCreateSwapChainForCoreWindow = nullptr;
static PFN_CreateSwapChainForComposition g_pfnOriginalCreateSwapChainForComposition = nullptr;

static thread_local bool g_inPresent = false;

// IPC and Frame Pacing
static HANDLE g_hPacingMap = NULL;
static AetherPulsePacingIPC* g_pPacingData = nullptr;
static HANDLE g_hTelemetryMap = NULL;
static TelemetrySharedMemory* g_pTelemetryData = nullptr;
static HANDLE g_hFsrMap = NULL;
static FSRSharedMemory* g_pFsrData = nullptr;

static HANDLE g_hWaitableTimer = NULL;
static LARGE_INTEGER g_qpcFreq = { 0 };
static LARGE_INTEGER g_lastPresentQpc = { 0 };
static int64_t g_nextFrameTargetQpc = 0;
static uint32_t g_lastTargetFps = 0;
static uint32_t g_lastMultiplier = 0;
static uint64_t g_frameCount = 0;
static bool g_pacerInitialized = false;

void EnsureSystemDxgiLoaded()
{
    if (!g_hSystemDxgi)
    {
        wchar_t sysPath[MAX_PATH];
        GetSystemDirectoryW(sysPath, MAX_PATH);
        wcscat_s(sysPath, L"\\dxgi.dll");
        g_hSystemDxgi = LoadLibraryW(sysPath);
        if (g_hSystemDxgi)
        {
            g_pfnSysCreateDXGIFactory  = (PFN_CreateDXGIFactory)GetProcAddress(g_hSystemDxgi, "CreateDXGIFactory");
            g_pfnSysCreateDXGIFactory1 = (PFN_CreateDXGIFactory1)GetProcAddress(g_hSystemDxgi, "CreateDXGIFactory1");
            g_pfnSysCreateDXGIFactory2 = (PFN_CreateDXGIFactory2)GetProcAddress(g_hSystemDxgi, "CreateDXGIFactory2");
            LogDXGI("[AetherPulse DXGI] Loaded System DXGI from: %ls\n", sysPath);
        }
    }
}

void InitializePacerEngine()
{
    if (g_pacerInitialized) return;

    timeBeginPeriod(1);
    QueryPerformanceFrequency(&g_qpcFreq);
    QueryPerformanceCounter(&g_lastPresentQpc);
    g_nextFrameTargetQpc = g_lastPresentQpc.QuadPart;

    g_hWaitableTimer = CreateWaitableTimerExW(NULL, NULL, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
    if (!g_hWaitableTimer)
    {
        g_hWaitableTimer = CreateWaitableTimerW(NULL, FALSE, NULL);
    }

    g_pacerInitialized = true;
    LogDXGI("[AetherPulse DXGI] Pacer Engine Initialized (QPC Freq: %llu Hz)\n", g_qpcFreq.QuadPart);
}

void PollIPCConnections()
{
    if (!g_hPacingMap || !g_pPacingData)
    {
        if (g_hPacingMap) { CloseHandle(g_hPacingMap); g_hPacingMap = NULL; }
        
        g_hPacingMap = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, L"Global\\AetherPulse_Pacing_IPC");
        if (!g_hPacingMap) g_hPacingMap = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, L"Local\\AetherPulse_Pacing_IPC");
        if (!g_hPacingMap) g_hPacingMap = OpenFileMappingW(FILE_MAP_READ | FILE_MAP_WRITE, FALSE, L"Global\\AetherPulse_Pacing_IPC");
        if (!g_hPacingMap) g_hPacingMap = OpenFileMappingW(FILE_MAP_READ, FALSE, L"Global\\AetherPulse_Pacing_IPC");
        if (!g_hPacingMap) g_hPacingMap = OpenFileMappingW(FILE_MAP_READ, FALSE, L"Local\\AetherPulse_Pacing_IPC");

        if (g_hPacingMap)
        {
            g_pPacingData = (AetherPulsePacingIPC*)MapViewOfFile(g_hPacingMap, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(AetherPulsePacingIPC));
            if (!g_pPacingData)
            {
                g_pPacingData = (AetherPulsePacingIPC*)MapViewOfFile(g_hPacingMap, FILE_MAP_READ, 0, 0, sizeof(AetherPulsePacingIPC));
            }
            if (g_pPacingData)
            {
                g_pPacingData->IsHookActive = 1;
                LogDXGI("[AetherPulse DXGI] Successfully mapped AetherPulse_Pacing_IPC (TargetFps: %u, EnablePacing: %u)\n", g_pPacingData->TargetFps, (uint32_t)g_pPacingData->EnablePacing);
            }
        }
    }

    if (!g_hTelemetryMap || !g_pTelemetryData)
    {
        if (g_hTelemetryMap) { CloseHandle(g_hTelemetryMap); g_hTelemetryMap = NULL; }

        g_hTelemetryMap = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, L"Local\\AetherPulseSharedMem");
        if (!g_hTelemetryMap) g_hTelemetryMap = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, L"Global\\AetherPulseSharedMem");
        if (!g_hTelemetryMap)
        {
            g_hTelemetryMap = CreateFileMappingW(INVALID_HANDLE_VALUE, NULL, PAGE_READWRITE, 0, sizeof(TelemetrySharedMemory), L"Local\\AetherPulseSharedMem");
        }

        if (g_hTelemetryMap)
        {
            g_pTelemetryData = (TelemetrySharedMemory*)MapViewOfFile(g_hTelemetryMap, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(TelemetrySharedMemory));
            if (g_pTelemetryData)
            {
                g_pTelemetryData->Signature = 0x4150544D; // "APTM"
                g_pTelemetryData->ProcessId = GetCurrentProcessId();
            }
        }
    }

    if (!g_hFsrMap || !g_pFsrData)
    {
        if (g_hFsrMap) { CloseHandle(g_hFsrMap); g_hFsrMap = NULL; }

        g_hFsrMap = OpenFileMappingW(FILE_MAP_READ, FALSE, L"Global\\AetherPulse_FSR_IPC");
        if (!g_hFsrMap) g_hFsrMap = OpenFileMappingW(FILE_MAP_READ, FALSE, L"Local\\AetherPulse_FSR_IPC");

        if (g_hFsrMap)
        {
            g_pFsrData = (FSRSharedMemory*)MapViewOfFile(g_hFsrMap, FILE_MAP_READ, 0, 0, sizeof(FSRSharedMemory));
        }
    }
}

static int64_t g_smoothedBaseIntervalQpc = 0;
static LARGE_INTEGER g_lastRealFrameQpc = { 0 };
static bool g_isInterpolatedFrame = false;
static double g_lastDeltaMs = 0.0;
static bool g_externalLimiterActive = false;
static uint32_t g_tightClampStreak = 0;

static inline bool CheckForExternalLimiterModules()
{
    return (GetModuleHandleW(L"RTSSHooks64.dll") != nullptr ||
            GetModuleHandleW(L"RTSSHooks.dll") != nullptr ||
            GetModuleHandleW(L"SpecialK64.dll") != nullptr ||
            GetModuleHandleW(L"SpecialK32.dll") != nullptr);
}

// Direct Swapchain RCAS Compute Pass (Fallback when FfxFsr dispatch is inactive)
class DirectRcasPass
{
public:
    static DirectRcasPass& Get()
    {
        static DirectRcasPass instance;
        return instance;
    }

    void Execute(IDXGISwapChain* pSwapChain, float sharpness)
    {
        if (!pSwapChain || sharpness <= 0.001f) return;

        __try
        {
            ID3D12Device* pDevice = nullptr;
            if (FAILED(pSwapChain->GetDevice(IID_PPV_ARGS(&pDevice))) || !pDevice)
            {
                return;
            }

            if (!m_initialized || m_pDevice != pDevice)
            {
                if (m_initialized) Shutdown();
                if (!Initialize(pDevice))
                {
                    pDevice->Release();
                    return;
                }
            }
            pDevice->Release();

            // Get Current Back Buffer
            IDXGISwapChain3* pSwapChain3 = nullptr;
            UINT backBufferIndex = 0;
            if (SUCCEEDED(pSwapChain->QueryInterface(IID_PPV_ARGS(&pSwapChain3))) && pSwapChain3)
            {
                backBufferIndex = pSwapChain3->GetCurrentBackBufferIndex();
                pSwapChain3->Release();
            }

            ID3D12Resource* pBackBuffer = nullptr;
            if (FAILED(pSwapChain->GetBuffer(backBufferIndex, IID_PPV_ARGS(&pBackBuffer))) || !pBackBuffer)
            {
                return;
            }

            D3D12_RESOURCE_DESC desc = pBackBuffer->GetDesc();
            if (desc.Width == 0 || desc.Height == 0)
            {
                pBackBuffer->Release();
                return;
            }

            // Ensure intermediate ping-pong buffer is allocated
            if (!m_pIntermediateBuffer || m_width != desc.Width || m_height != desc.Height || m_format != desc.Format)
            {
                CreateResources((UINT)desc.Width, (UINT)desc.Height, desc.Format);
            }

            if (m_pIntermediateBuffer && m_pCommandList && m_pCommandQueue && m_pDescriptorHeap)
            {
                m_pCommandAllocator->Reset();
                m_pCommandList->Reset(m_pCommandAllocator, m_pPipelineState);

                // Transition backbuffer to COPY_SOURCE and intermediate to COPY_DEST
                D3D12_RESOURCE_BARRIER barriers[2] = {};
                barriers[0].Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
                barriers[0].Transition.pResource = pBackBuffer;
                barriers[0].Transition.StateBefore = D3D12_RESOURCE_STATE_PRESENT;
                barriers[0].Transition.StateAfter = D3D12_RESOURCE_STATE_COPY_SOURCE;
                barriers[0].Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;

                barriers[1].Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
                barriers[1].Transition.pResource = m_pIntermediateBuffer;
                barriers[1].Transition.StateBefore = D3D12_RESOURCE_STATE_COMMON;
                barriers[1].Transition.StateAfter = D3D12_RESOURCE_STATE_COPY_DEST;
                barriers[1].Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
                m_pCommandList->ResourceBarrier(2, barriers);

                // Copy BackBuffer -> Intermediate
                m_pCommandList->CopyResource(m_pIntermediateBuffer, pBackBuffer);

                // Transition Intermediate -> NON_PIXEL_SHADER_RESOURCE, BackBuffer -> COPY_DEST
                barriers[0].Transition.StateBefore = D3D12_RESOURCE_STATE_COPY_SOURCE;
                barriers[0].Transition.StateAfter = D3D12_RESOURCE_STATE_COPY_DEST;

                barriers[1].Transition.StateBefore = D3D12_RESOURCE_STATE_COPY_DEST;
                barriers[1].Transition.StateAfter = D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE;
                m_pCommandList->ResourceBarrier(2, barriers);

                // Set compute pipeline state
                m_pCommandList->SetComputeRootSignature(m_pRootSignature);
                ID3D12DescriptorHeap* heaps[] = { m_pDescriptorHeap };
                m_pCommandList->SetDescriptorHeaps(1, heaps);

                struct {
                    uint32_t width;
                    uint32_t height;
                    float sharpness;
                    float padding;
                } constants = { (uint32_t)desc.Width, (uint32_t)desc.Height, sharpness, 0.0f };

                m_pCommandList->SetComputeRoot32BitConstants(0, 4, &constants, 0);
                m_pCommandList->SetComputeRootDescriptorTable(1, m_pDescriptorHeap->GetGPUDescriptorHandleForHeapStart());

                D3D12_GPU_DESCRIPTOR_HANDLE uavHandle = m_pDescriptorHeap->GetGPUDescriptorHandleForHeapStart();
                uavHandle.ptr += m_descriptorSize;
                m_pCommandList->SetComputeRootDescriptorTable(2, uavHandle);

                UINT dispatchX = ((UINT)desc.Width + 15) / 16;
                UINT dispatchY = ((UINT)desc.Height + 15) / 16;
                m_pCommandList->Dispatch(dispatchX, dispatchY, 1);

                // Transition backbuffer back to PRESENT and Intermediate to COMMON
                barriers[0].Transition.StateBefore = D3D12_RESOURCE_STATE_COPY_DEST;
                barriers[0].Transition.StateAfter = D3D12_RESOURCE_STATE_PRESENT;

                barriers[1].Transition.StateBefore = D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE;
                barriers[1].Transition.StateAfter = D3D12_RESOURCE_STATE_COMMON;
                m_pCommandList->ResourceBarrier(2, barriers);

                m_pCommandList->Close();
                ID3D12CommandList* cmdLists[] = { m_pCommandList };
                m_pCommandQueue->ExecuteCommandLists(1, cmdLists);

                if (!m_logged)
                {
                    m_logged = true;
                    LogDXGI("[AetherPulse DXGI] Direct Swapchain RCAS Pass Active: %ux%u, Sharpness=%.2f\n",
                            (UINT)desc.Width, (UINT)desc.Height, sharpness);
                }
            }

            pBackBuffer->Release();
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
        }
    }

    void SetCommandQueue(ID3D12CommandQueue* pQueue)
    {
        if (pQueue && pQueue != m_pCommandQueue)
        {
            if (m_pCommandQueue) m_pCommandQueue->Release();
            m_pCommandQueue = pQueue;
            m_pCommandQueue->AddRef();
        }
    }

private:
    bool m_initialized = false;
    bool m_logged = false;
    ID3D12Device* m_pDevice = nullptr;
    ID3D12CommandQueue* m_pCommandQueue = nullptr;
    ID3D12CommandAllocator* m_pCommandAllocator = nullptr;
    ID3D12GraphicsCommandList* m_pCommandList = nullptr;
    ID3D12RootSignature* m_pRootSignature = nullptr;
    ID3D12PipelineState* m_pPipelineState = nullptr;
    ID3D12DescriptorHeap* m_pDescriptorHeap = nullptr;
    ID3D12Resource* m_pIntermediateBuffer = nullptr;
    UINT m_width = 0;
    UINT m_height = 0;
    DXGI_FORMAT m_format = DXGI_FORMAT_UNKNOWN;
    UINT m_descriptorSize = 0;

    bool Initialize(ID3D12Device* pDevice)
    {
        m_pDevice = pDevice;
        m_pDevice->AddRef();

        D3D12_COMMAND_QUEUE_DESC qDesc = {};
        qDesc.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;
        if (FAILED(m_pDevice->CreateCommandQueue(&qDesc, IID_PPV_ARGS(&m_pCommandQueue))))
        {
            return false;
        }

        if (FAILED(m_pDevice->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&m_pCommandAllocator))))
        {
            return false;
        }

        if (FAILED(m_pDevice->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT, m_pCommandAllocator, nullptr, IID_PPV_ARGS(&m_pCommandList))))
        {
            return false;
        }
        m_pCommandList->Close();

        m_descriptorSize = m_pDevice->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV);

        // Compile RCAS Compute Shader
        const char rcasSource[] = R"(
            cbuffer RCASConstants : register(b0)
            {
                uint2 g_Resolution;
                float g_Sharpness;
                float g_Padding;
            };
            Texture2D<float4>   g_InputTexture   : register(t0);
            RWTexture2D<float4> g_OutputTexture  : register(u0);

            float RgbToLuma(float3 rgb) { return dot(rgb, float3(0.2126f, 0.7152f, 0.0722f)); }

            [numthreads(16, 16, 1)]
            void CSMain(uint3 dispatchThreadId : SV_DispatchThreadID)
            {
                if (dispatchThreadId.x >= g_Resolution.x || dispatchThreadId.y >= g_Resolution.y) return;
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
        HRESULT hr = D3DCompile(rcasSource, sizeof(rcasSource), "RCAS_CS", nullptr, nullptr, "CSMain", "cs_5_0", D3DCOMPILE_OPTIMIZATION_LEVEL3, 0, &pShaderBlob, &pErrorBlob);
        if (FAILED(hr))
        {
            if (pErrorBlob) pErrorBlob->Release();
            return false;
        }

        // Create Root Signature
        D3D12_ROOT_PARAMETER rootParams[3] = {};
        rootParams[0].ParameterType = D3D12_ROOT_PARAMETER_TYPE_32BIT_CONSTANTS;
        rootParams[0].Constants.ShaderRegister = 0;
        rootParams[0].Constants.Num32BitValues = 4;
        rootParams[0].ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;

        D3D12_DESCRIPTOR_RANGE srvRange = {};
        srvRange.RangeType = D3D12_DESCRIPTOR_RANGE_TYPE_SRV;
        srvRange.NumDescriptors = 1;
        srvRange.BaseShaderRegister = 0;
        srvRange.OffsetInDescriptorsFromTableStart = D3D12_DESCRIPTOR_RANGE_OFFSET_APPEND;
        rootParams[1].ParameterType = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
        rootParams[1].DescriptorTable.NumDescriptorRanges = 1;
        rootParams[1].DescriptorTable.pDescriptorRanges = &srvRange;
        rootParams[1].ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;

        D3D12_DESCRIPTOR_RANGE uavRange = {};
        uavRange.RangeType = D3D12_DESCRIPTOR_RANGE_TYPE_UAV;
        uavRange.NumDescriptors = 1;
        uavRange.BaseShaderRegister = 0;
        uavRange.OffsetInDescriptorsFromTableStart = D3D12_DESCRIPTOR_RANGE_OFFSET_APPEND;
        rootParams[2].ParameterType = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
        rootParams[2].DescriptorTable.NumDescriptorRanges = 1;
        rootParams[2].DescriptorTable.pDescriptorRanges = &uavRange;
        rootParams[2].ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;

        D3D12_ROOT_SIGNATURE_DESC rootSigDesc = {};
        rootSigDesc.NumParameters = 3;
        rootSigDesc.pParameters = rootParams;
        rootSigDesc.Flags = D3D12_ROOT_SIGNATURE_FLAG_NONE;

        ID3DBlob* pSigBlob = nullptr;
        hr = D3D12SerializeRootSignature(&rootSigDesc, D3D_ROOT_SIGNATURE_VERSION_1, &pSigBlob, nullptr);
        if (SUCCEEDED(hr))
        {
            m_pDevice->CreateRootSignature(0, pSigBlob->GetBufferPointer(), pSigBlob->GetBufferSize(), IID_PPV_ARGS(&m_pRootSignature));
            pSigBlob->Release();
        }

        // Create Compute PSO
        D3D12_COMPUTE_PIPELINE_STATE_DESC psoDesc = {};
        psoDesc.pRootSignature = m_pRootSignature;
        psoDesc.CS.pShaderBytecode = pShaderBlob->GetBufferPointer();
        psoDesc.CS.BytecodeLength = pShaderBlob->GetBufferSize();
        m_pDevice->CreateComputePipelineState(&psoDesc, IID_PPV_ARGS(&m_pPipelineState));
        pShaderBlob->Release();

        // Create Descriptor Heap (1 SRV + 1 UAV)
        D3D12_DESCRIPTOR_HEAP_DESC heapDesc = {};
        heapDesc.NumDescriptors = 2;
        heapDesc.Type = D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV;
        heapDesc.Flags = D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE;
        m_pDevice->CreateDescriptorHeap(&heapDesc, IID_PPV_ARGS(&m_pDescriptorHeap));

        m_initialized = (m_pPipelineState && m_pRootSignature && m_pDescriptorHeap);
        return m_initialized;
    }

    void CreateResources(UINT width, UINT height, DXGI_FORMAT format)
    {
        if (m_pIntermediateBuffer) { m_pIntermediateBuffer->Release(); m_pIntermediateBuffer = nullptr; }

        m_width = width;
        m_height = height;
        m_format = format;

        D3D12_HEAP_PROPERTIES heapProps = {};
        heapProps.Type = D3D12_HEAP_TYPE_DEFAULT;

        D3D12_RESOURCE_DESC rDesc = {};
        rDesc.Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
        rDesc.Width = width;
        rDesc.Height = height;
        rDesc.DepthOrArraySize = 1;
        rDesc.MipLevels = 1;
        rDesc.Format = format;
        rDesc.SampleDesc.Count = 1;
        rDesc.Flags = D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS;

        if (SUCCEEDED(m_pDevice->CreateCommittedResource(&heapProps, D3D12_HEAP_FLAG_NONE, &rDesc, D3D12_RESOURCE_STATE_COMMON, nullptr, IID_PPV_ARGS(&m_pIntermediateBuffer))))
        {
            // Setup SRV & UAV descriptors
            D3D12_CPU_DESCRIPTOR_HANDLE cpuHandle = m_pDescriptorHeap->GetCPUDescriptorHandleForHeapStart();
            m_pDevice->CreateShaderResourceView(m_pIntermediateBuffer, nullptr, cpuHandle);

            cpuHandle.ptr += m_descriptorSize;
            m_pDevice->CreateUnorderedAccessView(m_pIntermediateBuffer, nullptr, nullptr, cpuHandle);
        }
    }

    void Shutdown()
    {
        if (m_pIntermediateBuffer) { m_pIntermediateBuffer->Release(); m_pIntermediateBuffer = nullptr; }
        if (m_pDescriptorHeap) { m_pDescriptorHeap->Release(); m_pDescriptorHeap = nullptr; }
        if (m_pPipelineState) { m_pPipelineState->Release(); m_pPipelineState = nullptr; }
        if (m_pRootSignature) { m_pRootSignature->Release(); m_pRootSignature = nullptr; }
        if (m_pCommandList) { m_pCommandList->Release(); m_pCommandList = nullptr; }
        if (m_pCommandAllocator) { m_pCommandAllocator->Release(); m_pCommandAllocator = nullptr; }
        if (m_pCommandQueue) { m_pCommandQueue->Release(); m_pCommandQueue = nullptr; }
        if (m_pDevice) { m_pDevice->Release(); m_pDevice = nullptr; }
        m_initialized = false;
    }
};

void OnBeforePresent(IDXGISwapChain* pSwapChain, UINT syncInterval, UINT flags)
{
    InitializePacerEngine();
    PollIPCConnections();

    // Direct RCAS Swapchain Fallback: If RCAS is enabled in IPC, apply compute pass
    if (g_pFsrData && g_pFsrData->EnableRCAS && g_pFsrData->RCASSharpness > 0.001f)
    {
        DirectRcasPass::Get().Execute(pSwapChain, g_pFsrData->RCASSharpness);
    }

    // 1. Dynamic Per-Frame Detection: Check if RTSS / External Limiter hook is loaded in process
    bool rtssActive = CheckForExternalLimiterModules();
    if (rtssActive)
    {
        g_externalLimiterActive = true;
    }

    // 2. Publish state to IPC structures immediately
    if (g_pPacingData)
    {
        g_pPacingData->IsExternalLimiterActive = g_externalLimiterActive ? 1 : 0;
    }
    if (g_pTelemetryData)
    {
        g_pTelemetryData->IsExternalLimiterActive = g_externalLimiterActive ? 1 : 0;
    }

    LARGE_INTEGER currentQpc;
    QueryPerformanceCounter(&currentQpc);

    // 3. Absolute Sleep Bypass: If RTSS active or pacing disabled, return IMMEDIATELY (0ms wait)
    if (g_externalLimiterActive || !g_pPacingData || g_pPacingData->EnablePacing == 0 || g_pPacingData->MultiplierMode <= 1)
    {
        g_lastPresentQpc = currentQpc;
        return;
    }

    uint32_t multiplier = g_pPacingData->MultiplierMode;
    if (multiplier < 2) multiplier = 2;

    // Sub-frame Cadence Alignment for Frame Generation (50/50 interval pacing):
    // Smooths out real vs interpolated frame presentation cadence without artificial FPS ceiling.
    if (g_smoothedBaseIntervalQpc > 0 && g_isInterpolatedFrame)
    {
        int64_t subFrameIntervalQpc = g_smoothedBaseIntervalQpc / multiplier;
        int64_t targetQpc = g_lastPresentQpc.QuadPart + subFrameIntervalQpc;
        int64_t waitQpc = targetQpc - currentQpc.QuadPart;

        if (waitQpc > 0)
        {
            double waitMs = (double)waitQpc * 1000.0 / (double)g_qpcFreq.QuadPart;
            double maxWaitMs = ((double)subFrameIntervalQpc * 1000.0 / (double)g_qpcFreq.QuadPart) * 0.8;
            if (waitMs > maxWaitMs) waitMs = maxWaitMs;

            float spinThresholdMs = g_pPacingData->SpinWaitThresholdMs > 0.5f ? g_pPacingData->SpinWaitThresholdMs : 2.0f;
            if (waitMs > (double)spinThresholdMs && g_hWaitableTimer)
            {
                double sleepMs = waitMs - 1.0;
                if (sleepMs > 0.5)
                {
                    LARGE_INTEGER dueTime;
                    dueTime.QuadPart = -(LONGLONG)(sleepMs * 10000.0);
                    SetWaitableTimer(g_hWaitableTimer, &dueTime, 0, NULL, NULL, FALSE);
                    WaitForSingleObject(g_hWaitableTimer, (DWORD)(sleepMs + 5.0));
                }
            }

            while (true)
            {
                QueryPerformanceCounter(&currentQpc);
                if (currentQpc.QuadPart >= targetQpc)
                {
                    break;
                }
                _mm_pause();
            }
        }
    }
}

void OnAfterPresent(IDXGISwapChain* pSwapChain, HRESULT hr, UINT syncInterval, UINT flags)
{
    LARGE_INTEGER nowQpc;
    QueryPerformanceCounter(&nowQpc);

    double deltaMs = 0.0;
    if (g_frameCount > 0 && g_qpcFreq.QuadPart > 0 && g_lastPresentQpc.QuadPart > 0)
    {
        deltaMs = (double)(nowQpc.QuadPart - g_lastPresentQpc.QuadPart) * 1000.0 / (double)g_qpcFreq.QuadPart;
    }
    g_frameCount++;

    if (deltaMs > 0.1 && deltaMs < 500.0)
    {
        // External Limiter Auto-Detection: if consecutive frames exhibit tight delta stability (<150us jitter), RTSS/driver limiter is active
        if (g_lastDeltaMs > 0.001)
        {
            double diff = std::abs(deltaMs - g_lastDeltaMs);
            if (diff < 0.150)
            {
                if (++g_tightClampStreak > 8)
                    g_externalLimiterActive = true;
            }
            else if (diff > 0.750)
            {
                g_tightClampStreak = 0;
                g_externalLimiterActive = false;
            }
        }

        g_isInterpolatedFrame = !g_isInterpolatedFrame;
        if (!g_isInterpolatedFrame)
        {
            int64_t fullIntervalQpc = nowQpc.QuadPart - g_lastRealFrameQpc.QuadPart;
            if (fullIntervalQpc > 0 && fullIntervalQpc < g_qpcFreq.QuadPart)
            {
                double alpha = 0.050;
                if (!g_pPacingData || g_pPacingData->AutoEma != 0)
                {
                    double currentFps = deltaMs > 0.001 ? (1000.0 / deltaMs) : 60.0;
                    if (currentFps <= 40.0) alpha = 0.05;
                    else if (currentFps >= 144.0) alpha = 0.22;
                    else {
                        double t = (currentFps - 40.0) / (144.0 - 40.0);
                        alpha = 0.05 + t * (0.22 - 0.05);
                    }
                }
                else
                {
                    alpha = (g_pPacingData->ManualEmaAlpha > 0.001f) ? (double)g_pPacingData->ManualEmaAlpha : 0.050;
                }

                if (g_smoothedBaseIntervalQpc <= 0)
                    g_smoothedBaseIntervalQpc = fullIntervalQpc;
                else
                    g_smoothedBaseIntervalQpc = (int64_t)((1.0 - alpha) * (double)g_smoothedBaseIntervalQpc + alpha * (double)fullIntervalQpc);
            }
            g_lastRealFrameQpc = nowQpc;
        }

        // Cadence Ratio (Real vs Interpolated distribution, 0.50f = 50:50)
        float cadenceRatio = 0.50f;
        if (g_lastDeltaMs > 0.001)
        {
            double sumDelta = deltaMs + g_lastDeltaMs;
            if (sumDelta > 0.001)
            {
                cadenceRatio = (float)(deltaMs / sumDelta);
            }
        }

        // Sub-frame interval variance (microsecond jitter)
        float subFrameVarianceUs = 0.0f;
        if (g_lastDeltaMs > 0.001)
        {
            subFrameVarianceUs = (float)(std::abs(deltaMs - g_lastDeltaMs) * 1000.0);
        }

        g_lastDeltaMs = deltaMs;

        if (g_pTelemetryData)
        {
            g_pTelemetryData->FrametimeMs = (float)deltaMs;
            g_pTelemetryData->CurrentFPS = (float)(1000.0 / deltaMs);
            g_pTelemetryData->TotalPresentedFrames = g_frameCount;
            g_pTelemetryData->SyncInterval = syncInterval;
            g_pTelemetryData->PresentFlags = flags;
            g_pTelemetryData->CadenceRatio = cadenceRatio;
            g_pTelemetryData->SubFrameIntervalVarianceUs = subFrameVarianceUs;
            g_pTelemetryData->RealTimeDeltaMs = (float)deltaMs;
            g_pTelemetryData->IsExternalLimiterActive = g_externalLimiterActive ? 1 : 0;
        }
    }

    g_lastPresentQpc = nowQpc;

    if (g_frameCount % 300 == 1 && g_pPacingData)
    {
        LogDXGI("[AetherPulse DXGI] Cadence Present #%llu: Delta=%.2fms (FPS=%.1f) CadenceRatio=%.2f\n", g_frameCount, deltaMs, deltaMs > 0 ? 1000.0 / deltaMs : 0.0, g_pTelemetryData ? g_pTelemetryData->CadenceRatio : 0.5f);
    }
}

HRESULT WINAPI Hooked_Present(IDXGISwapChain* This, UINT SyncInterval, UINT Flags)
{
    if (g_inPresent)
    {
        return g_pfnOriginalPresent ? g_pfnOriginalPresent(This, SyncInterval, Flags) : This->Present(SyncInterval, Flags);
    }

    if (g_frameCount % 60 == 1)
    {
        LogDXGI("[AetherPulse DXGI] Hooked_Present invoked! Frame: %llu, TargetFps: %u\n", g_frameCount, g_pPacingData ? g_pPacingData->TargetFps : 0);
    }

    g_inPresent = true;
    OnBeforePresent(This, SyncInterval, Flags);

    HRESULT hr = g_pfnOriginalPresent ? g_pfnOriginalPresent(This, SyncInterval, Flags) : This->Present(SyncInterval, Flags);

    OnAfterPresent(This, hr, SyncInterval, Flags);
    g_inPresent = false;
    return hr;
}

HRESULT WINAPI Hooked_Present1(IDXGISwapChain1* This, UINT SyncInterval, UINT Flags, const DXGI_PRESENT_PARAMETERS* pPresentParameters)
{
    if (g_inPresent)
    {
        return g_pfnOriginalPresent1 ? g_pfnOriginalPresent1(This, SyncInterval, Flags, pPresentParameters) : This->Present1(SyncInterval, Flags, pPresentParameters);
    }

    if (g_frameCount % 60 == 1)
    {
        LogDXGI("[AetherPulse DXGI] Hooked_Present1 invoked! Frame: %llu, TargetFps: %u\n", g_frameCount, g_pPacingData ? g_pPacingData->TargetFps : 0);
    }

    g_inPresent = true;
    OnBeforePresent(This, SyncInterval, Flags);

    HRESULT hr = g_pfnOriginalPresent1 ? g_pfnOriginalPresent1(This, SyncInterval, Flags, pPresentParameters) : This->Present1(SyncInterval, Flags, pPresentParameters);

    OnAfterPresent(This, hr, SyncInterval, Flags);
    g_inPresent = false;
    return hr;
}

void HookSwapChainVMT(IDXGISwapChain* pSwapChain)
{
    if (!pSwapChain) return;

    void** vtbl = *reinterpret_cast<void***>(pSwapChain);
    if (!vtbl) return;

    LogDXGI("[AetherPulse DXGI] HookSwapChainVMT examining SwapChain %p (VTable: %p)\n", pSwapChain, vtbl);

    // Index 8: IDXGISwapChain::Present
    if (vtbl[8] != reinterpret_cast<void*>(Hooked_Present))
    {
        DWORD oldProtect = 0;
        if (VirtualProtect(&vtbl[8], sizeof(void*), PAGE_EXECUTE_READWRITE, &oldProtect))
        {
            if (!g_pfnOriginalPresent)
            {
                g_pfnOriginalPresent = reinterpret_cast<PFN_Present>(vtbl[8]);
            }
            vtbl[8] = reinterpret_cast<void*>(Hooked_Present);
            VirtualProtect(&vtbl[8], sizeof(void*), oldProtect, &oldProtect);
            LogDXGI("[AetherPulse DXGI] Attached Hooked_Present to VMT Index 8 (Original: %p)\n", g_pfnOriginalPresent);
        }
    }

    // Index 22: IDXGISwapChain1::Present1
    IDXGISwapChain1* pSwapChain1 = nullptr;
    if (SUCCEEDED(pSwapChain->QueryInterface(IID_PPV_ARGS(&pSwapChain1))) && pSwapChain1)
    {
        void** vtbl1 = *reinterpret_cast<void***>(pSwapChain1);
        if (vtbl1 && vtbl1[22] != reinterpret_cast<void*>(Hooked_Present1))
        {
            DWORD oldProtect = 0;
            if (VirtualProtect(&vtbl1[22], sizeof(void*), PAGE_EXECUTE_READWRITE, &oldProtect))
            {
                if (!g_pfnOriginalPresent1)
                {
                    g_pfnOriginalPresent1 = reinterpret_cast<PFN_Present1>(vtbl1[22]);
                }
                vtbl1[22] = reinterpret_cast<void*>(Hooked_Present1);
                VirtualProtect(&vtbl1[22], sizeof(void*), oldProtect, &oldProtect);
                LogDXGI("[AetherPulse DXGI] Attached Hooked_Present1 to VMT Index 22 (Original: %p)\n", g_pfnOriginalPresent1);
            }
        }
        pSwapChain1->Release();
    }
}

// Factory Hooks
HRESULT WINAPI Hooked_CreateSwapChain(IDXGIFactory* This, IUnknown* pDevice, DXGI_SWAP_CHAIN_DESC* pDesc, IDXGISwapChain** ppSwapChain)
{
    LogDXGI("[AetherPulse DXGI] Hooked_CreateSwapChain called on Factory %p\n", This);
    HRESULT hr = g_pfnOriginalCreateSwapChain ? g_pfnOriginalCreateSwapChain(This, pDevice, pDesc, ppSwapChain) : E_FAIL;
    if (SUCCEEDED(hr) && ppSwapChain && *ppSwapChain)
    {
        LogDXGI("[AetherPulse DXGI] CreateSwapChain succeeded, returned SwapChain: %p\n", *ppSwapChain);
        HookSwapChainVMT(*ppSwapChain);
    }
    return hr;
}

HRESULT WINAPI Hooked_CreateSwapChainForHwnd(IDXGIFactory2* This, IUnknown* pDevice, HWND hWnd, const DXGI_SWAP_CHAIN_DESC1* pDesc, const DXGI_SWAP_CHAIN_FULLSCREEN_DESC* pFullscreenDesc, IDXGIOutput* pRestrictToOutput, IDXGISwapChain1** ppSwapChain)
{
    LogDXGI("[AetherPulse DXGI] Hooked_CreateSwapChainForHwnd called on Factory %p (HWND: %p)\n", This, hWnd);
    HRESULT hr = g_pfnOriginalCreateSwapChainForHwnd ? g_pfnOriginalCreateSwapChainForHwnd(This, pDevice, hWnd, pDesc, pFullscreenDesc, pRestrictToOutput, ppSwapChain) : E_FAIL;
    if (SUCCEEDED(hr) && ppSwapChain && *ppSwapChain)
    {
        LogDXGI("[AetherPulse DXGI] CreateSwapChainForHwnd succeeded, returned SwapChain1: %p\n", *ppSwapChain);
        HookSwapChainVMT(*ppSwapChain);
    }
    return hr;
}

HRESULT WINAPI Hooked_CreateSwapChainForCoreWindow(IDXGIFactory2* This, IUnknown* pDevice, IUnknown* pWindow, const DXGI_SWAP_CHAIN_DESC1* pDesc, IDXGIOutput* pRestrictToOutput, IDXGISwapChain1** ppSwapChain)
{
    LogDXGI("[AetherPulse DXGI] Hooked_CreateSwapChainForCoreWindow called on Factory %p\n", This);
    HRESULT hr = g_pfnOriginalCreateSwapChainForCoreWindow ? g_pfnOriginalCreateSwapChainForCoreWindow(This, pDevice, pWindow, pDesc, pRestrictToOutput, ppSwapChain) : E_FAIL;
    if (SUCCEEDED(hr) && ppSwapChain && *ppSwapChain)
    {
        LogDXGI("[AetherPulse DXGI] CreateSwapChainForCoreWindow succeeded, returned SwapChain1: %p\n", *ppSwapChain);
        HookSwapChainVMT(*ppSwapChain);
    }
    return hr;
}

HRESULT WINAPI Hooked_CreateSwapChainForComposition(IDXGIFactory2* This, IUnknown* pDevice, const DXGI_SWAP_CHAIN_DESC1* pDesc, IDXGIOutput* pRestrictToOutput, IDXGISwapChain1** ppSwapChain)
{
    LogDXGI("[AetherPulse DXGI] Hooked_CreateSwapChainForComposition called on Factory %p\n", This);
    HRESULT hr = g_pfnOriginalCreateSwapChainForComposition ? g_pfnOriginalCreateSwapChainForComposition(This, pDevice, pDesc, pRestrictToOutput, ppSwapChain) : E_FAIL;
    if (SUCCEEDED(hr) && ppSwapChain && *ppSwapChain)
    {
        LogDXGI("[AetherPulse DXGI] CreateSwapChainForComposition succeeded, returned SwapChain1: %p\n", *ppSwapChain);
        HookSwapChainVMT(*ppSwapChain);
    }
    return hr;
}

void HookFactoryVMT(void* pFactory)
{
    if (!pFactory) return;

    void** vtbl = *reinterpret_cast<void***>(pFactory);
    if (!vtbl) return;

    LogDXGI("[AetherPulse DXGI] HookFactoryVMT examining Factory %p (VTable: %p)\n", pFactory, vtbl);

    // Hook IDXGIFactory::CreateSwapChain (Index 10)
    if (vtbl[10] != reinterpret_cast<void*>(Hooked_CreateSwapChain))
    {
        DWORD oldProtect = 0;
        if (VirtualProtect(&vtbl[10], sizeof(void*), PAGE_EXECUTE_READWRITE, &oldProtect))
        {
            if (!g_pfnOriginalCreateSwapChain)
            {
                g_pfnOriginalCreateSwapChain = reinterpret_cast<PFN_CreateSwapChain>(vtbl[10]);
            }
            vtbl[10] = reinterpret_cast<void*>(Hooked_CreateSwapChain);
            VirtualProtect(&vtbl[10], sizeof(void*), oldProtect, &oldProtect);
            LogDXGI("[AetherPulse DXGI] Attached Hooked_CreateSwapChain to Factory Index 10\n");
        }
    }

    // Check if IDXGIFactory2 (Index 15: CreateSwapChainForHwnd, Index 16: CreateSwapChainForCoreWindow, Index 24: CreateSwapChainForComposition)
    IDXGIFactory2* pFactory2 = nullptr;
    IUnknown* pUnk = reinterpret_cast<IUnknown*>(pFactory);
    if (SUCCEEDED(pUnk->QueryInterface(IID_PPV_ARGS(&pFactory2))) && pFactory2)
    {
        void** vtbl2 = *reinterpret_cast<void***>(pFactory2);
        if (vtbl2)
        {
            // Index 15: CreateSwapChainForHwnd
            if (vtbl2[15] != reinterpret_cast<void*>(Hooked_CreateSwapChainForHwnd))
            {
                DWORD oldProtect = 0;
                if (VirtualProtect(&vtbl2[15], sizeof(void*), PAGE_EXECUTE_READWRITE, &oldProtect))
                {
                    if (!g_pfnOriginalCreateSwapChainForHwnd)
                    {
                        g_pfnOriginalCreateSwapChainForHwnd = reinterpret_cast<PFN_CreateSwapChainForHwnd>(vtbl2[15]);
                    }
                    vtbl2[15] = reinterpret_cast<void*>(Hooked_CreateSwapChainForHwnd);
                    VirtualProtect(&vtbl2[15], sizeof(void*), oldProtect, &oldProtect);
                    LogDXGI("[AetherPulse DXGI] Attached Hooked_CreateSwapChainForHwnd to Factory2 Index 15\n");
                }
            }

            // Index 16: CreateSwapChainForCoreWindow
            if (vtbl2[16] != reinterpret_cast<void*>(Hooked_CreateSwapChainForCoreWindow))
            {
                DWORD oldProtect = 0;
                if (VirtualProtect(&vtbl2[16], sizeof(void*), PAGE_EXECUTE_READWRITE, &oldProtect))
                {
                    if (!g_pfnOriginalCreateSwapChainForCoreWindow)
                    {
                        g_pfnOriginalCreateSwapChainForCoreWindow = reinterpret_cast<PFN_CreateSwapChainForCoreWindow>(vtbl2[16]);
                    }
                    vtbl2[16] = reinterpret_cast<void*>(Hooked_CreateSwapChainForCoreWindow);
                    VirtualProtect(&vtbl2[16], sizeof(void*), oldProtect, &oldProtect);
                    LogDXGI("[AetherPulse DXGI] Attached Hooked_CreateSwapChainForCoreWindow to Factory2 Index 16\n");
                }
            }

            // Index 24: CreateSwapChainForComposition
            if (vtbl2[24] != reinterpret_cast<void*>(Hooked_CreateSwapChainForComposition))
            {
                DWORD oldProtect = 0;
                if (VirtualProtect(&vtbl2[24], sizeof(void*), PAGE_EXECUTE_READWRITE, &oldProtect))
                {
                    if (!g_pfnOriginalCreateSwapChainForComposition)
                    {
                        g_pfnOriginalCreateSwapChainForComposition = reinterpret_cast<PFN_CreateSwapChainForComposition>(vtbl2[24]);
                    }
                    vtbl2[24] = reinterpret_cast<void*>(Hooked_CreateSwapChainForComposition);
                    VirtualProtect(&vtbl2[24], sizeof(void*), oldProtect, &oldProtect);
                    LogDXGI("[AetherPulse DXGI] Attached Hooked_CreateSwapChainForComposition to Factory2 Index 24\n");
                }
            }
        }
        pFactory2->Release();
    }
}

// DXGI Factory Proxies
extern "C" {
    HRESULT WINAPI Hooked_CreateDXGIFactory(REFIID riid, void** ppFactory)
    {
        LogDXGI("[AetherPulse DXGI] CreateDXGIFactory called\n");
        EnsureSystemDxgiLoaded();
        if (!g_pfnSysCreateDXGIFactory) return E_FAIL;
        HRESULT hr = g_pfnSysCreateDXGIFactory(riid, ppFactory);
        if (SUCCEEDED(hr) && ppFactory && *ppFactory)
        {
            HookFactoryVMT(*ppFactory);
        }
        return hr;
    }

    HRESULT WINAPI Hooked_CreateDXGIFactory1(REFIID riid, void** ppFactory)
    {
        LogDXGI("[AetherPulse DXGI] CreateDXGIFactory1 called\n");
        EnsureSystemDxgiLoaded();
        if (!g_pfnSysCreateDXGIFactory1) return E_FAIL;
        HRESULT hr = g_pfnSysCreateDXGIFactory1(riid, ppFactory);
        if (SUCCEEDED(hr) && ppFactory && *ppFactory)
        {
            HookFactoryVMT(*ppFactory);
        }
        return hr;
    }

    HRESULT WINAPI Hooked_CreateDXGIFactory2(UINT Flags, REFIID riid, void** ppFactory)
    {
        LogDXGI("[AetherPulse DXGI] CreateDXGIFactory2 called (Flags: 0x%X)\n", Flags);
        EnsureSystemDxgiLoaded();
        if (!g_pfnSysCreateDXGIFactory2) return E_FAIL;
        HRESULT hr = g_pfnSysCreateDXGIFactory2(Flags, riid, ppFactory);
        if (SUCCEEDED(hr) && ppFactory && *ppFactory)
        {
            HookFactoryVMT(*ppFactory);
        }
        return hr;
    }

    __declspec(dllexport) HRESULT WINAPI CompatString(void* p1, void* p2, void* p3, void* p4) { return E_NOTIMPL; }
    __declspec(dllexport) HRESULT WINAPI CompatValue(void* p1, void* p2) { return E_NOTIMPL; }
    __declspec(dllexport) HRESULT WINAPI DXGICreateGlobalKeyedMutex(void* p1, void* p2) { return E_NOTIMPL; }
    __declspec(dllexport) HRESULT WINAPI DXGIOpenGlobalKeyedMutex(void* p1, void* p2) { return E_NOTIMPL; }
    __declspec(dllexport) HRESULT WINAPI PIXBeginCapture(void* p1, void* p2) { return E_NOTIMPL; }
    __declspec(dllexport) HRESULT WINAPI PIXEndCapture(void* p1) { return E_NOTIMPL; }
    __declspec(dllexport) DWORD   WINAPI PIXGetCaptureState() { return 0; }
    __declspec(dllexport) HRESULT WINAPI SetAppCompatStringPointer(void* p1, void* p2) { return E_NOTIMPL; }
    __declspec(dllexport) HRESULT WINAPI UpdateOverlaySupport(void* p1) { return E_NOTIMPL; }
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved)
{
    if (ul_reason_for_call == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(hModule);
        LogDXGI("[AetherPulse DXGI] DLL_PROCESS_ATTACH in process: %lu\n", GetCurrentProcessId());
        InitializePacerEngine();
    }
    else if (ul_reason_for_call == DLL_PROCESS_DETACH)
    {
        LogDXGI("[AetherPulse DXGI] DLL_PROCESS_DETACH\n");
        if (g_pPacingData)
        {
            g_pPacingData->IsHookActive = 0;
            UnmapViewOfFile(g_pPacingData);
            g_pPacingData = nullptr;
        }
        if (g_hPacingMap)
        {
            CloseHandle(g_hPacingMap);
            g_hPacingMap = NULL;
        }
        if (g_pTelemetryData)
        {
            UnmapViewOfFile(g_pTelemetryData);
            g_pTelemetryData = nullptr;
        }
        if (g_hTelemetryMap)
        {
            CloseHandle(g_hTelemetryMap);
            g_hTelemetryMap = NULL;
        }
        if (g_hWaitableTimer)
        {
            CloseHandle(g_hWaitableTimer);
            g_hWaitableTimer = NULL;
        }
        if (g_hSystemDxgi)
        {
            FreeLibrary(g_hSystemDxgi);
            g_hSystemDxgi = NULL;
        }
        timeEndPeriod(1);
    }
    return TRUE;
}
