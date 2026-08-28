# AetherPulse ⚡ [WIP / IN ACTIVE DEVELOPMENT]

> ⚠️ **IMPORTANT NOTICE / WORK IN PROGRESS:**  
> **Most advanced rendering features (FSR 4 Upscaling, Neural Ray Reconstruction, Driver Multi-Frame Gen Interop) are currently experimental, under active development, or non-functional placeholders.**  
> The currently working and testable feature is the **Direct3D 12 SwapChain Presentation Frame Pacer (`version.dll`)** for smoothing frame delivery and cadence tracking.

---

## 📌 Overview

**AetherPulse** is an open-source Direct3D 12 rendering diagnostic and frame-pacing utility designed for modern single-player games. It utilizes a lightweight native chainloader proxy (`version.dll`) alongside a desktop tuning dashboard to stabilize frame delivery, eliminate micro-stutter, and interface with cutting-edge graphics pipelines.

---

## 🚦 Feature Roadmap & Current Status

| Component | Status | Description |
| :--- | :---: | :--- |
| **D3D12 Presentation Frame Pacer** | ✅ **Working** | High-precision waitable timer pacing, EMA frame-time cadence alignment, and 1% low smoothing. |
| **DirectX 12 In-Game Telemetry Overlay** | ✅ **Working** | Real-time FPS, frame-time (ms), 1% lows, and jitter percentage tracking. |
| **Game Library & Hook Deployment** | ✅ **Working** | Auto-detection, conflict cleaning (OptiScaler/ReShade safety), and proxy staging. |
| **Anti-Lag 2 SDK Synchronization** | 🟡 **Partial** | CPU/GPU submission alignment hooks. |
| **Driver Multi-Frame Generation Interop** | 🔴 **WIP / Disabled** | Experimental AGS driver-level presentation latching. |
| **FSR 4 Next-Gen Upscaling** | 🔴 **In Development** | ML-assisted reconstruction interposer hooks (Preview only). |
| **FidelityFX Ray Regeneration (RR)** | 🔴 **In Development** | D3D12 NRC and wavelet denoising pass interposition. |

---

## ⚠️ Anti-Cheat & Multiplayer Notice

> **HAZARD WARNING:** Proxy DLL injection (`version.dll`, `dxgi.dll`) is **strictly intended for offline and single-player games**. Online titles equipped with anti-cheat software (*Easy Anti-Cheat, BattlEye, Vanguard, Ricochet, etc.*) will detect proxy DLLs and may issue immediate, permanent account bans. Always verify your target game before deploying.

---

## 🛠️ Architecture & Tech Stack

* **Native Hook Core (`version.dll`):** Modern C++20, Direct3D 12, DXGI 1.6, Win32 High-Resolution Waitable Timers.
* **Tuning GUI (`AetherPulse.exe`):** C# / .NET 8.0, WPF, MVVM Toolkit.
* **Shared State Pipeline:** Low-latency memory-mapped / JSON status heartbeat file (`aetherpulse_status.json`).

---

## 📄 License
Licensed under the [MIT License](LICENSE).