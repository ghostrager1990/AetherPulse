# AetherPulse v1.1.1

**AMD FidelityFX & FSR Bridge Suite**  
*Real-time DirectX 12 / Streamline Frame Pacing, Multi-Frame Generation, FidelityFX Ray Regeneration, and FSR Upscaling Engine.*

---

## 🚀 Overview

AetherPulse is a high-performance runtime bridging layer designed for modern DirectX 12 titles. It intercepts presentation queues and Streamline evaluation pipelines to deliver:

- **Sub-Millisecond DXGI Frame Pacing:** Eliminates micro-stuttering during Frame Generation playback via hybrid high-resolution waitable timers (`CREATE_WAITABLE_TIMER_HIGH_RESOLUTION`).
- **Multi-Frame Generation (1x to 6x):** Dispatches $(N - 1)$ intermediate optical flow passes per native frame present, unlocking high-cadence fluid motion for 180Hz–360Hz displays.
- **Adaptive Dynamic Target FPS:** Automatically shifts optical flow multipliers between 1x and 6x to maintain a user-defined target frame rate without unnecessary GPU load.
- **Live In-Game Hot-Reload (Zero Restarts):** Bidirectional Shared Memory IPC applies slider and toggle adjustments to active render loops in real time.
- **FidelityFX Ray Regeneration & Denoising:** Real-time wavelet radiance filtering for ray-traced lighting pipelines without vendor lock-in.
- **Neural Radiance Caching (NRC):** Multi-bounce indirect lighting probe caching and acceleration via `amd_fidelityfx_radiancecache_dx12.dll`.
- **DLSS-to-FSR Bridge:** Intercepts DLSS / Streamline execution paths and maps them directly into AMD FidelityFX compute pipelines with Native AA and DRS clamping.
- **Live Telemetry & Diagnostics:** Microsecond-precision presentation cadence, jitter analysis, and hardware status dashboard.

---

## ⚡ Quick Start Guide

Getting up and running takes less than a minute:

1. **Step 1: Add & Deploy via Game Library**  
   Launch `AetherPulse.exe`, navigate to the **Game Library**, scan or manually browse to your game's executable (`.exe`), and click **Deploy**. AetherPulse places the lightweight proxy hooks and official AMD FidelityFX runtime modules directly into the game directory.
2. **Step 2: Enable DLSS / Frame Generation In-Game**  
   Launch the game and open the Graphics/Display settings. Turn ON **DLSS** and/or **DLSS Frame Generation / Ray Reconstruction**. AetherPulse seamlessly intercepts these calls and executes official AMD FidelityFX Super Resolution, Optical Flow Frame Gen, and NRC pipelines under the hood.
3. **Step 3: Live Tuning in the Background (Zero Restarts)**  
   Keep AetherPulse open while gaming. Adjust RCAS Sharpening, Frame Gen Multipliers (2x–6x or Adaptive), Negative Mipmap LOD Bias, or Wavelet Denoising sliders—all changes serialize live to `aetherpulse.ini` and apply instantly without restarting.

### Quick Recommended Configurations

- **Competitive / High FPS:** Native AA Mode ON + 2x/3x Frame Gen + Anti-Lag 2 ON *(Pristine native edge clarity, ultra-low input latency)*.
- **Path Tracing / Visual Immersion:** FSR Quality (67%) + NRC ON + Balanced Denoising + 2x Frame Gen *(Flicker-free ray-traced reflections and multi-bounce global illumination)*.
- **High Refresh Display (144Hz–360Hz):** Adaptive Target FPS Mode set to your display's native refresh rate *(Dynamic optical flow scaling for locked cadence)*.

---

## ⚙️ Architecture & Modular Runtime Suite

- **`AppUI` (.NET 8 / WPF):** Hardware-accelerated desktop control center built with MVVM, dynamic telemetry graphs, process monitoring, and automatic game compatibility scanning.
- **`NativeCore` (C++20 / D3D12 / MSVC x64):** Ultra-low-overhead DXGI proxy (`dxgi.dll`), Streamline (`sl.interposer.dll`) hook, and direct dispatch bridges.
- **Modular AMD FidelityFX Runtime Modules:**

| Module | Description |
| :--- | :--- |
| `amd_fidelityfx_upscaler_dx12.dll` | AMD FidelityFX Super Resolution temporal reconstruction and Native AA engine. |
| `amd_fidelityfx_framegeneration_dx12.dll` | Optical flow frame generation and swapchain interpolation runtime. |
| `amd_fidelityfx_denoiser_dx12.dll` | FidelityFX Ray Regeneration spatial/temporal wavelet radiance filter. |
| `amd_fidelityfx_radiancecache_dx12.dll` | Neural Radiance Cache (NRC) multi-bounce global illumination probe accelerator. |
| `amd_fidelityfx_loader_dx12.dll` | Dynamic module loader and validation runtime layer. |
| `amd_antilag2_dx12.dll` | AMD Radeon Anti-Lag 2 SDK CPU-GPU synchronization layer. |
| `amd_ags_x64.dll` & `amd_acs_x64.dll` | AMD GPU Services and Compute Services hardware communication layers. |
| `RCAS_CS.cso` | Post-upscaled Robust Contrast Adaptive Sharpening (RCAS) compute pass. |

---

## 🛡️ Anti-Cheat & Fair Play Notice

AetherPulse is intended exclusively for offline and single-player titles. The integrated launcher includes signature scanning and high-risk interlocks to warn users against injecting proxy DLLs into competitive online games.

---

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
