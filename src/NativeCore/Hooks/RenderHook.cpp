#include <windows.h>
#include <d3d12.h>
#include <dxgi1_4.h>
#include <fstream>
#include <chrono>
#include <vector>
#include <cmath>
#include <algorithm>
#include <string>

#pragma comment(linker, "/export:GetFileVersionInfoA=C:\\Windows\\System32\\version.GetFileVersionInfoA")
#pragma comment(linker, "/export:GetFileVersionInfoByHandle=C:\\Windows\\System32\\version.GetFileVersionInfoByHandle")
#pragma comment(linker, "/export:GetFileVersionInfoExA=C:\\Windows\\System32\\version.GetFileVersionInfoExA")
#pragma comment(linker, "/export:GetFileVersionInfoExW=C:\\Windows\\System32\\version.GetFileVersionInfoExW")
#pragma comment(linker, "/export:GetFileVersionInfoSizeA=C:\\Windows\\System32\\version.GetFileVersionInfoSizeA")
#pragma comment(linker, "/export:GetFileVersionInfoSizeExA=C:\\Windows\\System32\\version.GetFileVersionInfoSizeExA")
#pragma comment(linker, "/export:GetFileVersionInfoSizeExW=C:\\Windows\\System32\\version.GetFileVersionInfoSizeExW")
#pragma comment(linker, "/export:GetFileVersionInfoSizeW=C:\\Windows\\System32\\version.GetFileVersionInfoSizeW")
#pragma comment(linker, "/export:GetFileVersionInfoW=C:\\Windows\\System32\\version.GetFileVersionInfoW")
#pragma comment(linker, "/export:VerFindSourceA=C:\\Windows\\System32\\version.VerFindSourceA")
#pragma comment(linker, "/export:VerFindSourceW=C:\\Windows\\System32\\version.VerFindSourceW")
#pragma comment(linker, "/export:VerInstallFileA=C:\\Windows\\System32\\version.VerInstallFileA")
#pragma comment(linker, "/export:VerInstallFileW=C:\\Windows\\System32\\version.VerInstallFileW")
#pragma comment(linker, "/export:VerLanguageNameA=C:\\Windows\\System32\\version.VerLanguageNameA")
#pragma comment(linker, "/export:VerLanguageNameW=C:\\Windows\\System32\\version.VerLanguageNameW")
#pragma comment(linker, "/export:VerQueryValueA=C:\\Windows\\System32\\version.VerQueryValueA")
#pragma comment(linker, "/export:VerQueryValueW=C:\\Windows\\System32\\version.VerQueryValueW")

#pragma comment(lib, "d3d12.lib")
#pragma comment(lib, "dxgi.lib")
#pragma comment(lib, "user32.lib")

struct RuntimeConfig {
    bool enablePacing = true;
    int targetFps = 0;
    bool overrideRCAS = true;
    float rcasSharpness = 0.35f;
};

static RuntimeConfig g_Config;
static uint64_t g_FrameCount = 0;
static double g_FrametimeMs = 0.0;
static double g_OnePercentLowFps = 0.0;
static double g_StutterPercent = 0.0;

typedef HRESULT(WINAPI* PFN_Present)(IDXGISwapChain*, UINT, UINT);
typedef HRESULT(WINAPI* PFN_Present1)(IDXGISwapChain1*, UINT, UINT, const DXGI_PRESENT_PARAMETERS*);

static PFN_Present g_OriginalPresent = nullptr;
static PFN_Present1 g_OriginalPresent1 = nullptr;

void ReadConfig() {
    std::ifstream file("C:\\Users\\Public\\aetherpulse.ini");
    if (!file.is_open()) return;
    std::string line;
    while (std::getline(file, line)) {
        try {
            if (line.find("enablePacing=") != std::string::npos) g_Config.enablePacing = std::stoi(line.substr(line.find("=") + 1)) != 0;
            else if (line.find("targetFps=") != std::string::npos) g_Config.targetFps = std::stoi(line.substr(line.find("=") + 1));
            else if (line.find("overrideRCAS=") != std::string::npos) g_Config.overrideRCAS = std::stoi(line.substr(line.find("=") + 1)) != 0;
            else if (line.find("rcasSharpness=") != std::string::npos) g_Config.rcasSharpness = std::stof(line.substr(line.find("=") + 1));
        } catch (...) {}
    }
}

void WriteStatus() {
    std::ofstream file("C:\\Users\\Public\\aetherpulse_status.json");
    if (!file.is_open()) return;
    file << "{\n"
         << "  \"pid\": " << GetCurrentProcessId() << ",\n"
         << "  \"frames\": " << g_FrameCount << ",\n"
         << "  \"frametimeMs\": " << g_FrametimeMs << ",\n"
         << "  \"onePercentLowFps\": " << g_OnePercentLowFps << ",\n"
         << "  \"stutterPercent\": " << g_StutterPercent << ",\n"
         << "  \"targetFps\": " << g_Config.targetFps << ",\n"
         << "  \"rcasSharpness\": " << g_Config.rcasSharpness << ",\n"
         << "  \"timestamp\": " << GetTickCount64() << ",\n"
         << "  \"pacing\": " << (g_Config.enablePacing ? "true" : "false") << ",\n"
         << "  \"rayRegen\": true\n"
         << "}\n";
}

void ProcessFrameCadence() {
    static auto frameTargetDeadline = std::chrono::high_resolution_clock::now();
    static auto lastPresentTime = std::chrono::high_resolution_clock::now();

    // 1. Precise frame interval pacing
    if (g_Config.enablePacing && g_Config.targetFps > 0) {
        auto targetInterval = std::chrono::nanoseconds((uint64_t)(1000000000.0 / g_Config.targetFps));
        
        // Spin-wait until target deadline is reached
        while (std::chrono::high_resolution_clock::now() < frameTargetDeadline) {
            YieldProcessor();
        }
        
        auto now = std::chrono::high_resolution_clock::now();
        if (now > frameTargetDeadline + targetInterval) {
            frameTargetDeadline = now + targetInterval;
        } else {
            frameTargetDeadline += targetInterval;
        }
    } else {
        frameTargetDeadline = std::chrono::high_resolution_clock::now();
    }

    auto now = std::chrono::high_resolution_clock::now();
    g_FrametimeMs = std::chrono::duration<double, std::milli>(now - lastPresentTime).count();
    lastPresentTime = now;
    g_FrameCount++;

    static std::vector<double> history;
    history.push_back(g_FrametimeMs);
    if (history.size() > 120) history.erase(history.begin());
    if (!history.empty()) {
        std::vector<double> sorted = history;
        std::sort(sorted.begin(), sorted.end());
        size_t idx = (size_t)(sorted.size() * 0.99);
        double lowTime = sorted[idx < sorted.size() ? idx : sorted.size() - 1];
        g_OnePercentLowFps = lowTime > 0.001 ? (1000.0 / lowTime) : 0.0;

        double avg = 0.0;
        for (double d : history) avg += d;
        avg /= history.size();
        double variance = 0.0;
        for (double d : history) variance += std::abs(d - avg);
        g_StutterPercent = avg > 0.001 ? ((variance / history.size()) / avg) * 100.0 : 0.0;
    }

    if (g_FrameCount % 10 == 0) {
        ReadConfig();
        WriteStatus();
    }
}

HRESULT WINAPI HookedPresent(IDXGISwapChain* pSwapChain, UINT SyncInterval, UINT Flags) {
    ProcessFrameCadence();
    return g_OriginalPresent ? g_OriginalPresent(pSwapChain, SyncInterval, Flags) : S_OK;
}

HRESULT WINAPI HookedPresent1(IDXGISwapChain1* pSwapChain, UINT SyncInterval, UINT Flags, const DXGI_PRESENT_PARAMETERS* pPresentParameters) {
    ProcessFrameCadence();
    return g_OriginalPresent1 ? g_OriginalPresent1(pSwapChain, SyncInterval, Flags, pPresentParameters) : S_OK;
}

DWORD WINAPI HookInitThread(LPVOID) {
    Sleep(1200);

    WNDCLASSA wc = { 0 };
    wc.lpfnWndProc = DefWindowProcA;
    wc.hInstance = GetModuleHandle(nullptr);
    wc.lpszClassName = "AetherPulseDummy";
    RegisterClassA(&wc);
    HWND hwnd = CreateWindowA(wc.lpszClassName, "Dummy", WS_OVERLAPPEDWINDOW, 0, 0, 100, 100, nullptr, nullptr, wc.hInstance, nullptr);

    IDXGIFactory4* pFactory = nullptr;
    CreateDXGIFactory1(IID_PPV_ARGS(&pFactory));

    ID3D12Device* pDevice = nullptr;
    D3D12CreateDevice(nullptr, D3D_FEATURE_LEVEL_11_0, IID_PPV_ARGS(&pDevice));

    if (pFactory && pDevice && hwnd) {
        D3D12_COMMAND_QUEUE_DESC queueDesc = { D3D12_COMMAND_LIST_TYPE_DIRECT };
        ID3D12CommandQueue* pQueue = nullptr;
        pDevice->CreateCommandQueue(&queueDesc, IID_PPV_ARGS(&pQueue));

        DXGI_SWAP_CHAIN_DESC1 scDesc = {};
        scDesc.BufferCount = 2;
        scDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
        scDesc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
        scDesc.SampleDesc.Count = 1;
        scDesc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;

        IDXGISwapChain1* pSwapChain1 = nullptr;
        pFactory->CreateSwapChainForHwnd(pQueue, hwnd, &scDesc, nullptr, nullptr, &pSwapChain1);

        if (pSwapChain1) {
            void** vtbl = *(void***)pSwapChain1;
            DWORD oldProtect;

            VirtualProtect(&vtbl[8], sizeof(void*), PAGE_EXECUTE_READWRITE, &oldProtect);
            g_OriginalPresent = (PFN_Present)vtbl[8];
            vtbl[8] = (void*)HookedPresent;
            VirtualProtect(&vtbl[8], sizeof(void*), oldProtect, &oldProtect);

            VirtualProtect(&vtbl[22], sizeof(void*), PAGE_EXECUTE_READWRITE, &oldProtect);
            g_OriginalPresent1 = (PFN_Present1)vtbl[22];
            vtbl[22] = (void*)HookedPresent1;
            VirtualProtect(&vtbl[22], sizeof(void*), oldProtect, &oldProtect);

            pSwapChain1->Release();
        }
        if (pQueue) pQueue->Release();
        pDevice->Release();
        pFactory->Release();
    }
    DestroyWindow(hwnd);
    UnregisterClassA("AetherPulseDummy", wc.hInstance);
    return 0;
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID reserved) {
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(hModule);
        CreateThread(nullptr, 0, HookInitThread, nullptr, 0, nullptr);
    }
    return TRUE;
}
