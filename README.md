# AetherPulse `v1.1.1`

**AMD FidelityFX & FSR 4 Bridge Suite**  
*Real-time DirectX 12 / Streamline Frame Pacing, FidelityFX Ray Regeneration, and FSR 4 Upscaling Engine.*

---

## 🚀 Overview

AetherPulse is a high-performance runtime bridging layer designed for modern DirectX 12 titles. It intercepts presentation queues and Streamline evaluation pipelines to deliver:

- **Sub-Millisecond DXGI Frame Pacing:** Eliminates micro-stuttering during Frame Generation playback via hybrid high-resolution waitable timers.
- **FidelityFX Ray Regeneration:** Real-time wavelet radiance filtering for ray-traced lighting pipelines without vendor lock-in.
- **Neural Radiance Caching (NRC):** Multi-bounce indirect lighting probe caching and acceleration via `amd_fidelityfx_radiancecache_dx12.dll`.
- **DLSS-to-FSR 4 Bridge:** Intercepts DLSS / Streamline execution paths and maps them directly into AMD FidelityFX FSR 4 compute pipelines with Native AA and Reactive Mask optimization.
- **Live Telemetry & Diagnostics:** Microsecond-precision presentation cadence, jitter analysis, and hardware status dashboard.

---

## ⚙️ Architecture & Modular Runtime Suite

- **`AppUI` (.NET / WPF):** Hardware-accelerated, modern desktop control center built with MVVM, dynamic telemetry graphs, and automatic game compatibility scanning.
- **`NativeCore` (C++ / D3D12 / MSVC):** Ultra-low-overhead DXGI proxy (`dxgi.dll`), Streamline (`sl.interposer.dll`) hook, and direct dispatch bridges.
- **Modular AMD FidelityFX Runtime Modules:**

| Module | Description |
| :--- | :--- |
| `amd_fidelityfx_upscaler_dx12.dll` | AMD FidelityFX Super Resolution temporal reconstruction and Native AA engine. |
| `amd_fidelityfx_framegeneration_dx12.dll` | Optical flow frame generation and swapchain interpolation runtime. |
| `amd_fidelityfx_denoiser_dx12.dll` | FidelityFX Ray Regeneration spatial/temporal wavelet radiance filter. |
| `amd_fidelityfx_radiancecache_dx12.dll` | Neural Radiance Cache (NRC) multi-bounce global illumination probe accelerator. |
| `amd_antilag2_dx12.dll` | AMD Radeon Anti-Lag 2 SDK CPU-GPU synchronization layer. |
| `amd_ags_x64.dll` & `amd_acs_x64.dll` | AMD GPU Services and Compute Services hardware communication layers. |

---

## 🛡️ Anti-Cheat & Fair Play Notice

AetherPulse is intended exclusively for offline and single-player titles. The integrated launcher includes signature scanning and high-risk interlocks to warn users against injecting proxy DLLs into competitive online games.

---

## 📦 Installation & Testing

1. Download the pre-built archive from [Releases](https://github.com/ghostrager1990/AetherPulse/releases).
2. Extract the package to any local folder.
3. Launch `AetherPulse.exe`, select your target game executable, and deploy hooks.
4. In-game, enable DLSS / Ray Reconstruction in the graphics menu to utilize the FidelityFX / FSR 4 bridge.

## 🖥️ Recommended Display & Sync Settings

- **Variable Refresh Rate (VRR / FreeSync / G-Sync):**
  - **In-Game VSync:** `OFF`
  - **Monitor VRR:** `ON`
  - **Frame Rate Cap:** Set 3–4 FPS below your display's maximum refresh rate (e.g., 140 FPS on a 144Hz panel) to prevent presentation queue overrun.
- **Fixed Refresh Displays (60Hz / 120Hz):**
  - Enable AetherPulse DXGI Frame Pacing or standard VSync to eliminate screen tearing during frame generation playback.
- **Frame Generation Best Practice:**
  - Disable in-game VSync whenever Frame Generation (AFMF / FSR-FG) is enabled to avoid presentation queue latency and judder.

---

*Developed by Stanorius Software © 2026*
