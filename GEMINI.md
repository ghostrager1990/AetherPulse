# AetherPulse - Antigravity Agent Directives

## 1. Context & Token-Saving Protocol
- **Terse & Direct Execution:** Minimize conversational meta-chatter and preamble. State the action taken, the file modified, and the result.
- **Diff-Only Updates:** When modifying existing files, output only modified functions, properties, or unified diff hunks rather than printing full source files.
- **No Speculative Reads:** Do not recursively scan or ingest directories under build/, bin/, obj/, .vs/, or vendor/ unless explicitly requested.
- **Strict Single-Task Scope:** Implement only the specific stage or sub-task requested. Focus strictly on Native Frame Pacing, FidelityFX SDK / RCAS compute passes, and UI Configuration/Payload Deployment.

---

## 2. Core Architecture & Primary Goal
AetherPulse is a standalone DirectX 12 render hook and configuration manager designed to provide:
1. **Universal Chainloader Architecture:** Loading via a lightweight version.dll proxy shim that routes initialization to AetherPulseCore.dll.
2. **DXGI Half-Interval & Exact Frame Pacing:** Metering IDXGISwapChain::Present / Present1 with high-resolution QPC spin-yield timers.
3. **Modular FidelityFX SDK & RCAS Pipeline:** Dynamic loading of official SDK binaries (amd_fidelityfx_loader_dx12.dll, amd_fidelityfx_upscaler_dx12.dll, amd_fidelityfx_framegeneration_dx12.dll, etc.) from payload/sdk/ alongside an embedded swapchain D3D12 RCAS compute shader pass.
4. **Live Telemetry & Status Sync:** Atomic JSON telemetry writer (C:\Users\Public\aetherpulse_status.json) delivering PID, frametime, 1% lows, stutter variance, and active feature flags.
5. **Always-On-Top Floating HUD:** Pin toggle and opacity slider in WPF title bar for live tuning over active game windows.

---

## 3. Technical Constraints & Code Standards

### Native Core (C++20 / DirectX 12 / AetherPulseCore.dll & version.dll):
- **Proxy & Entry Standard:** Use standard version.dll exports forwarding to C:\Windows\System32\version.dll to load AetherPulseCore.dll.
- **Hook Attachment:** Spawn background worker thread on DLL_PROCESS_ATTACH hooking SwapChain Present (vtable slot 8) and Present1 (vtable slot 22) via a dummy window/device pattern.
- **Command Queue Capture:** Hook ID3D12CommandQueue::ExecuteCommandLists (vtable slot 10) to reliably capture the game's direct command queue for compute dispatches.
- **Direct D3D12 RCAS Dispatch:** Maintain a dedicated Root Signature, Compute Pipeline State Object (PSO), and intermediate UAV resource to execute the FidelityFX RCAS compute kernel (cs_5_0) directly on backbuffer transitions before presentation.
- **Dynamic Modular SDK Integration:** Search and resolve amd_fidelityfx_loader_dx12.dll dynamically from root or payload\sdk\, proxying ffxCreateContext, ffxConfigure, and ffxDispatch.
- **Live Status File:** Write C:\Users\Public\aetherpulse_status.json on a 10-frame cadence with PID, frametimes, 1% low FPS, target FPS, and RCAS sharpness.

### UI Manager (C# .NET 8 / WPF):
- **Execution Level:** Standard non-elevated user mode (asInvoker).
- **MVVM Framework:** Use CommunityToolkit.Mvvm ([ObservableProperty], [RelayCommand]).
- **Overlay & In-Game Usability:** Bind Window.Topmost to IsAlwaysOnTop and Window.Opacity to OverlayOpacity (0.30–1.00) in MainViewModel.
- **Target Directory Resolution:** Always resolve deployment and cleanup paths against Path.GetDirectoryName(target.ExecutablePath) to ensure subdirectories (e.g., bin64\) are correctly targeted.
- **Deployment Packaging:** Automatically mirror Assets\Payload\*.dll to the game's executable directory and payload\sdk\ folder on Deploy.
- **Clean Uninstall:** When clicking Uninstall Hook, purge version.dll, AetherPulseCore.dll, aetherpulse.ini, and the payload\ folder without deleting native game engine dependencies.

---

## 4. Safety & Build Rules
- Keep CMake and MSVC compiler configurations targeting x64 only (/std:c++20 /EHsc /O2 /MD /LD).
- Do not hardcode internal game memory offsets. All image sharpening and pacing must operate strictly through standardized D3D12 compute pipelines and swapchain present hooks.
- Anti-Cheat Pre-Check: Block injection on known anti-cheat protected executables (EasyAntiCheat, BattlEye, Vanguard, Ricochet, etc.) unless acknowledged by user.
