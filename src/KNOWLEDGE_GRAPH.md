# Knowledge Graph & Architecture Map

## Module Entities & Responsibilities
- **AppUI (`src/AppUI/`)**: WPF .NET 8 UI. Admin elevated.
  - `PacingIpcService.cs` -> Writes `AetherPulsePacingIPC` (22 bytes, pack=1) to `Global\AetherPulse_Pacing_IPC`.
  - `FsrIpcService.cs` -> Writes `FSRSharedMemory` to `Local\AetherPulse_FSR_IPC`.
- **NativeCore (`src/NativeCore/`)**: C++ DLL wrappers deployed to game directory.
  - `dxgi_main.cpp` -> Intercepts `CreateDXGIFactory*`, hooks SwapChain VMT indices 8 (`Present`) and 22 (`Present1`).
  - `FramePacer.cpp` -> Reads `Global\AetherPulse_Pacing_IPC`. High-resolution waitable timer + QPC spinlock throttle.
  - `sl_main.cpp` / `StreamlineBridge.cpp` -> Streamline interposer bridge.

## Critical Invariants & Rules
- **IPC Alignment**: Structs must use `#pragma pack(push, 1)` and strictly match C# byte lengths.
- **Hook Lifecycle**: Factory creation -> SwapChain creation -> VMT substitution (`Present`, `Present1`).
- **Binary Deploy Target**: `G:\Games\Crimson Desert\bin64\`.