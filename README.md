# AetherPulse ⚡

> Real-time system diagnostics, resource telemetry, and performance orchestration built for high-demand Windows workloads.

[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%2F%2011%20x64-0078D6?logo=windows)](https://github.com/ghostrager1990/AetherPulse)
[![Framework](https://img.shields.io/badge/framework-.NET%2010%20%7C%20WPF-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Author](https://img.shields.io/badge/author-Stanorius%20Software-orange)](https://ghostrager1990.github.io)

---

## ⚡ Vision & Architecture

**AetherPulse** is a lightweight, low-overhead performance telemetry and resource orchestration utility designed to give gamers, power users, and creators granular control over their runtime environment. 

### Key Highlights
- **Zero-Bloat Telemetry:** Near-zero CPU cycle footprint during active sampling.
- **Hardware-Aware Diagnostics:** Native interfacing with Windows APIs to track GPU/CPU loads, memory allocations, and standby state caches.
- **Modern Fluent UI:** Polished WPF interface built with seamless dark-mode styling.
- **Portable & Self-Contained:** No external installer dependencies or runtime prerequisites needed.

---

## 🚀 Getting Started

### Prerequisites
- Windows 10 (1903+) or Windows 11 (x64)
- .NET 10 Desktop Runtime

### Installation
1. Head over to the **[Releases](https://github.com/ghostrager1990/AetherPulse/releases)** page.
2. Download the latest release package.
3. Extract and run `AetherPulse.exe`.

---

## 🛠️ Building from Source

To compile and package the solution locally:

# Clone the repository
git clone [https://github.com/ghostrager1990/AetherPulse.git](https://github.com/ghostrager1990/AetherPulse.git)
cd AetherPulse

# Build the solution
dotnet build -c Release

# Publish a single-file, self-contained executable
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./publish

# Clone the repository
git clone [https://github.com/ghostrager1990/AetherPulse.git](https://github.com/ghostrager1990/AetherPulse.git)
cd AetherPulse

# Build the solution
dotnet build -c Release

# Publish a single-file, self-contained executable
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./publish

# 📜 Roadmap
[ ] DirectML integration for predictive memory leak mitigation

[ ] Custom tray minimization with live metric mini-indicators

[ ] Hotkey-driven process priority toggling for full-screen workloads

# 📄 License
Distributed under the MIT License. See LICENSE for more information.

Developed with care by Stanorius Software.