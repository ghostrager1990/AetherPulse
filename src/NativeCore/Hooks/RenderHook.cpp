#include <windows.h>
#include <d3d12.h>
#include <dxgi1_4.h>
#include <fstream>
#include <chrono>
#include <vector>
#include <cmath>
#include <algorithm>
#include <string>
#include <atomic>

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

#ifndef CREATE_WAITABLE_TIMER_HIGH_RESOLUTION
#define CREATE_WAITABLE_TIMER_HIGH_RESOLUTION 0x00000002
#endif

struct RuntimeConfig {
    std::atomic<bool> enablePacing{ true };
    std::atomic<int> targetFps{ 0 };
    std::atomic<bool> overrideRCAS{ true };
    std::atomic<float> rcasSharpness{ 0.35f };
};

static RuntimeConfig g_Config;
static std::atomic<bool> g_Running{ true };
static uint64_t g_FrameCount = 0;
static double g_FrametimeMs = 0.0;
static double g_OnePercentLowFps = 0.0;
static double g_StutterPercent = 0.0;
static double g_PacingJitterMs = 0.0;
static int g_DrainUncapFrames = 0;

static HANDLE g_hWaitableTimer = nullptr;
static LARGE_INTEGER g_qpcFreq{ 0 };

typedef HRESULT(WINAPI* PFN_Present)(IDXGISwapChain*, UINT, UINT);
typedef HRESULT(WINAPI* PFN_Present1)(IDXGISwapChain1*, UINT, UINT, const DXGI_PRESENT_PARAMETERS*);

static PFN_Present g_OriginalPresent = nullptr;
static PFN_Present1 g_OriginalPresent1 = nullptr;

static std::string Trim(const std::string& str) {
    size_t first = str.find_first_not_of(" \t\r\n");
    if (first == std::string::npos) return "";
    size_t last = str.find_last_not_of(" \t\r\n");
    return str.substr(first, (last - first + 1));
}

void ReadConfigFile() {
    std::ifstream file("C:\\Users\\Public\\aetherpulse.ini");
    if (!file.is_open()) return;

    std::string line;
    while (std::getline(file, line)) {
        line = Trim(line);
        if (line.empty() || line[0] == ';' || line[0] == '#') continue;

        size_t eq = line.find('=');
        if (eq == std::string::npos) continue;

        std::string key = Trim(line.substr(0, eq));
        std::string val = Trim(line.substr(eq + 1));

        try {
            if (_stricmp(key.c_str(), "targetFps") == 0 || _stricmp(key.c_str(), "targetFpsCap") == 0) {
                g_Config.targetFps.store(std::stoi(val));
            }
            else if (_stricmp(key.c_str(), "enablePacing") == 0) {
                g_Config.enablePacing.store(_stricmp(val.c_str(), "true") == 0 || val == "1");
            }
            else if (_stricmp(key.c_str(), "overrideRCAS") == 0 || _stricmp(key.c_str(), "RCASSharpening") == 0) {
                g_Config.overrideRCAS.store(_stricmp(val.c_str(), "true") == 0 || val == "1");
            }
            else if (_stricmp(key.c_str(), "rcasSharpness") == 0 || _stricmp(key.c_str(), "sharpness") == 0) {
                g_Config.rcasSharpness.store(std::stof(val));
            }
        } catch (...) {}
    }
}

DWORD WINAPI ConfigWatcherThread(LPVOID) {
    while (g_Running.load()) {
        ReadConfigFile();
        Sleep(20);
    }
    return 0;
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
         << "  \"pacingJitterMs\": " << g_PacingJitterMs << ",\n"
         << "  \"targetFps\": " << g_Config.targetFps.load() << ",\n"
         << "  \"rcasSharpness\": " << g_Config.rcasSharpness.load() << ",\n"
         << "  \"timestamp\": " << GetTickCount64() << ",\n"
         << "  \"pacing\": " << (g_Config.enablePacing.load() ? "true" : "false") << ",\n"
         << "  \"rayRegen\": true\n"
         << "}\n";
}

void ProcessCadence() {
    static int64_t lastPresentQpc = 0;
    static int lastCap = 0;
    
    LARGE_INTEGER nowQpc;
    QueryPerformanceCounter(&nowQpc);
    int64_t currentQpc = nowQpc.QuadPart;

    int targetCap = g_Config.targetFps.load();
    bool pacingEnabled = g_Config.enablePacing.load();

    if (targetCap != lastCap) {
        lastCap = targetCap;
        lastPresentQpc = currentQpc;
        g_PacingJitterMs = 0.0;
        if (targetCap <= 0) {
            g_DrainUncapFrames = 30;
        }
        return;
    }

    if (lastPresentQpc == 0) {
        lastPresentQpc = currentQpc;
        return;
    }

    if (pacingEnabled && targetCap > 0) {
        int64_t targetIntervalQpc = (g_qpcFreq.QuadPart) / targetCap;
        int64_t targetDeadlineQpc = lastPresentQpc + targetIntervalQpc;

        if (targetDeadlineQpc > currentQpc) {
            int64_t ticksRemaining = targetDeadlineQpc - currentQpc;
            int64_t sleep100Ns = -((ticksRemaining * 10000000LL) / g_qpcFreq.QuadPart);
            if (sleep100Ns < -5000 && g_hWaitableTimer) {
                LARGE_INTEGER dueTime;
                dueTime.QuadPart = sleep100Ns;
                if (SetWaitableTimer(g_hWaitableTimer, &dueTime, 0, nullptr, nullptr, FALSE)) {
                    WaitForSingleObject(g_hWaitableTimer, INFINITE);
                }
            }

            QueryPerformanceCounter(&nowQpc);
            while (nowQpc.QuadPart < targetDeadlineQpc) {
                YieldProcessor();
                QueryPerformanceCounter(&nowQpc);
            }
        }

        QueryPerformanceCounter(&nowQpc);
        int64_t actualPresentQpc = nowQpc.QuadPart;
        double jitterTicks = (double)std::abs(actualPresentQpc - targetDeadlineQpc);
        g_PacingJitterMs = (jitterTicks * 1000.0) / (double)g_qpcFreq.QuadPart;
    } else {
        g_PacingJitterMs = 0.0;
        if (g_DrainUncapFrames > 0) {
            g_DrainUncapFrames--;
            lastPresentQpc = currentQpc;
        }
    }

    QueryPerformanceCounter(&nowQpc);
    int64_t deltaTicks = nowQpc.QuadPart - lastPresentQpc;
    g_FrametimeMs = ((double)deltaTicks * 1000.0) / (double)g_qpcFreq.QuadPart;
    lastPresentQpc = nowQpc.QuadPart;
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
        WriteStatus();
    }
}

HRESULT WINAPI HookedPresent(IDXGISwapChain* pSwapChain, UINT SyncInterval, UINT Flags) {
    ProcessCadence();
    if (g_DrainUncapFrames > 0 && SyncInterval > 0) {
        SyncInterval = 0;
    }
    return g_OriginalPresent ? g_OriginalPresent(pSwapChain, SyncInterval, Flags) : S_OK;
}

HRESULT WINAPI HookedPresent1(IDXGISwapChain1* pSwapChain, UINT SyncInterval, UINT Flags, const DXGI_PRESENT_PARAMETERS* pPresentParameters) {
    ProcessCadence();
    if (g_DrainUncapFrames > 0 && SyncInterval > 0) {
        SyncInterval = 0;
    }
    return g_OriginalPresent1 ? g_OriginalPresent1(pSwapChain, SyncInterval, Flags, pPresentParameters) : S_OK;
}

DWORD WINAPI HookInitThread(LPVOID) {
    Sleep(1200);

    QueryPerformanceFrequency(&g_qpcFreq);
    g_hWaitableTimer = CreateWaitableTimerExW(nullptr, nullptr, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
    if (!g_hWaitableTimer) {
        g_hWaitableTimer = CreateWaitableTimerExW(nullptr, nullptr, 0, TIMER_ALL_ACCESS);
    }

    CreateThread(nullptr, 0, ConfigWatcherThread, nullptr, 0, nullptr);

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

namespace RenderHook {
    DWORD WINAPI InitHookThread(LPVOID param) {
        return HookInitThread(param);
    }

    void Shutdown() {
        g_Running.store(false);
        if (g_hWaitableTimer) {
            CloseHandle(g_hWaitableTimer);
            g_hWaitableTimer = nullptr;
        }
    }
}