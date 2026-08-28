#include <windows.h>
#include <d3d12.h>
#include <fstream>
#include <string>

typedef struct FfxFsrDispatchDescription {
    ID3D12GraphicsCommandList* commandList;
    ID3D12Resource* color;
    ID3D12Resource* depth;
    ID3D12Resource* motionVectors;
    ID3D12Resource* exposure;
    ID3D12Resource* reactive;
    ID3D12Resource* transparencyAndComposition;
    ID3D12Resource* output;
    float jitterOffsetX;
    float jitterOffsetY;
    float motionVectorScaleX;
    float motionVectorScaleY;
    bool reset;
    float sharpness;
    float preExposure;
    float renderSizeX;
    float renderSizeY;
    bool enableSharpening;
} FfxFsrDispatchDescription;

typedef struct FfxContext {
    void* internalContext;
} FfxContext;

typedef uint64_t FfxErrorCode;

typedef FfxErrorCode(*PFN_ffxCreateContext)(FfxContext* context, void* desc, void* memAlloc);
typedef FfxErrorCode(*PFN_ffxConfigure)(FfxContext* context, void* desc);
typedef FfxErrorCode(*PFN_ffxDispatch)(FfxContext* context, const void* desc);
typedef FfxErrorCode(*PFN_ffxDestroyContext)(FfxContext* context, void* memAlloc);

static PFN_ffxCreateContext g_RealCreate = nullptr;
static PFN_ffxConfigure g_RealConfig = nullptr;
static PFN_ffxDispatch g_RealDispatch = nullptr;
static PFN_ffxDestroyContext g_RealDestroy = nullptr;
static HMODULE g_hSdk = nullptr;

struct RuntimeConfig {
    bool overrideRCAS = true;
    float rcasSharpness = 0.35f;
};
static RuntimeConfig g_Config;

void LoadSdk() {
    if (g_hSdk) return;
    g_hSdk = LoadLibraryA("payload\\sdk\\amd_fidelityfx_loader_dx12.dll");
    if (!g_hSdk) g_hSdk = LoadLibraryA("payload\\amd_fidelityfx_loader_dx12.dll");
    if (g_hSdk) {
        g_RealCreate = (PFN_ffxCreateContext)GetProcAddress(g_hSdk, "ffxCreateContext");
        g_RealConfig = (PFN_ffxConfigure)GetProcAddress(g_hSdk, "ffxConfigure");
        g_RealDispatch = (PFN_ffxDispatch)GetProcAddress(g_hSdk, "ffxDispatch");
        g_RealDestroy = (PFN_ffxDestroyContext)GetProcAddress(g_hSdk, "ffxDestroyContext");
    }
}

void ReadConfig() {
    std::ifstream file("C:\\Users\\Public\\aetherpulse.ini");
    if (!file.is_open()) return;
    std::string line;
    while (std::getline(file, line)) {
        try {
            if (line.find("overrideRCAS=") != std::string::npos) g_Config.overrideRCAS = std::stoi(line.substr(line.find("=") + 1)) != 0;
            else if (line.find("rcasSharpness=") != std::string::npos) g_Config.rcasSharpness = std::stof(line.substr(line.find("=") + 1));
        } catch (...) {}
    }
}

extern "C" __declspec(dllexport) FfxErrorCode ffxCreateContext(FfxContext* context, void* desc, void* memAlloc) {
    LoadSdk();
    return g_RealCreate ? g_RealCreate(context, desc, memAlloc) : 0;
}

extern "C" __declspec(dllexport) FfxErrorCode ffxConfigure(FfxContext* context, void* desc) {
    LoadSdk();
    return g_RealConfig ? g_RealConfig(context, desc) : 0;
}

extern "C" __declspec(dllexport) FfxErrorCode ffxDispatch(FfxContext* context, void* desc) {
    LoadSdk();
    if (desc) {
        ReadConfig();
        if (g_Config.overrideRCAS) {
            auto* fsr = reinterpret_cast<FfxFsrDispatchDescription*>(desc);
            fsr->sharpness = g_Config.rcasSharpness;
            fsr->enableSharpening = (g_Config.rcasSharpness > 0.001f);
        }
    }
    return g_RealDispatch ? g_RealDispatch(context, desc) : 0;
}

extern "C" __declspec(dllexport) FfxErrorCode ffxDestroyContext(FfxContext* context, void* memAlloc) {
    LoadSdk();
    return g_RealDestroy ? g_RealDestroy(context, memAlloc) : 0;
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID reserved) {
    if (reason == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(hModule);
    }
    return TRUE;
}
