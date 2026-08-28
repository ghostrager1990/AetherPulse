#pragma once
#include <d3d12.h>

namespace FsrRayRegenHook {
    bool Initialize();
    void Shutdown();
    void OnPrePresent(ID3D12CommandQueue* pQueue, ID3D12Resource* pRenderTarget);
}
