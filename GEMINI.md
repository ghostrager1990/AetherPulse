# AetherPulse - Antigravity Agent Directives

## 1. Context & Token-Saving Protocol
- **Terse & Direct Execution:** Minimize conversational meta-chatter and preamble. State the action taken, the file modified, and the result.
- **Diff-Only Updates:** When modifying existing files, output only the modified functions or unified diff hunks rather than printing full 500-line source files.
- **No Speculative Reads:** Do not recursively scan or ingest directories under `build/`, `bin/`, `obj/`, `.vs/`, or `vendor/` unless explicitly requested.
- **Strict Single-Task Scope:** Implement only the specific stage or sub-task requested. Do not preemptively generate code for future stages unprompted.

---

## 2. Architecture & Directory Boundaries
```text
AetherPulse/
├── src/
│   ├── NativeCore/       -> C++20 dynamic library (dxgi.dll / sl.interposer.dll)
│   │   ├── DXGI/         -> MinHook, EMA pacing, High-Resolution waitable timers
│   │   ├── Streamline/   -> DLSS-D slSetTag interception & resource cache
│   │   ├── FidelityFX/   -> AMD Ray Regeneration compute pass dispatches
│   │   └── Shared/       -> AetherTelemetry.h (struct pack(push, 1))
│   │
│   └── AppUI/            -> C# .NET 8 WPF Application (MVVM)
│       ├── Models/       -> Game profile and INI configuration models
│       ├── Services/     -> MemoryMappedFile reader, ProcessWatcher, Deployer
│       ├── ViewModels/   -> CommunityToolkit.Mvvm bindings
│       └── Views/        -> Dark-theme XAML views and pacing visualizers
└── aetherpulse.ini       -> Runtime config for pacing cadence and denoiser

## 3. Technical Constraints & Code Standards
Native Core (C++20 / DirectX 12):
Windows APIs: Use CreateWaitableTimerExW with CREATE_WAITABLE_TIMER_HIGH_RESOLUTION for half-interval pacing delays.

Shared Memory Struct Alignment: All shared structs in src/NativeCore/Shared/ must use #pragma pack(push, 1) to prevent 32/64-bit alignment mismatches with C# [StructLayout(LayoutKind.Sequential, Pack = 1)].

Resource Tracking: Ensure COM pointers (ID3D12Resource*, ID3D12Device*) are properly ref-counted or held as raw pointers without causing DirectX 12 device leaks.

Safety First: Validate all Streamline tags and D3D12 resource states before executing ffxDispatchDenoiser.

UI Manager (C# .NET 8 / WPF):
MVVM Framework: Use CommunityToolkit.Mvvm ([ObservableProperty], [RelayCommand]).

Telemetry Polling: Read the Local\AetherPulseTelemetry memory-mapped file asynchronously on a background timer (interval: 16ms / ~60 Hz) without blocking the UI dispatcher thread.

Error Handling: Gracefully handle file lock exceptions and administrator elevation prompts when deploying DLLs into protected game directories (e.g., Program Files).

## 4. Safety & Verification Rules
Do not execute destructive file system operations (e.g., recursive deletes) without an explicit warning and confirmation.

Keep CMake build configurations targeting x64 only (DirectX 12 Ray Tracing requires 64-bit targets).