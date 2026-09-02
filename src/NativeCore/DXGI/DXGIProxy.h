#pragma once

#include <windows.h>
#include <dxgi1_6.h>

void InitializeDXGIProxyAndHooks();
void ShutdownDXGIProxyAndHooks();
void HookSwapChain(IDXGISwapChain* pSwapChain);
void HookFactory(void* pFactory);
void HookSwapChainVMT(IDXGISwapChain* pSwapChain);
