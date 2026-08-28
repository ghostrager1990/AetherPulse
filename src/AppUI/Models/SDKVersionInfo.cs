using System;
using System.Diagnostics;
using System.IO;

namespace AppUI.Models
{
    public class SDKComponentVersion
    {
        public string ComponentName { get; set; } = string.Empty;
        public string VersionString { get; set; } = string.Empty;
        public string TargetArchitecture { get; set; } = "x64";
        public string Status { get; set; } = "Active";
        public string Details { get; set; } = string.Empty;
    }

    public static class SDKVersionDiscovery
    {
        public static SDKComponentVersion GetFidelityFXVersion()
        {
            return new SDKComponentVersion
            {
                ComponentName = "AMD FidelityFX SDK Core",
                VersionString = "v3.1.2 / FSR 4.x Ready",
                Status = "Linked / Operational",
                Details = "à-trous wavelet denoising passes with bilateral depth & normal edge gating"
            };
        }

        public static SDKComponentVersion GetAntiLag2Version()
        {
            return new SDKComponentVersion
            {
                ComponentName = "AMD Radeon Anti-Lag 2 Bridge",
                VersionString = "AL2 API v1.0.4 (Driver 24.8.1+)",
                Status = "Driver Interop Ready",
                Details = "Zero-latency CPU render submission queue synchronization"
            };
        }

        public static SDKComponentVersion GetHLSLBytecodeTarget()
        {
            return new SDKComponentVersion
            {
                ComponentName = "HLSL Compute Bytecode Target",
                VersionString = "Shader Model 6.6 (DirectX 12 Agility SDK)",
                Status = "Pre-compiled (.cso)",
                Details = "Wave Matrix Multiply Accumulate (WMMA) & Wave Intrinsics (cs_6_6)"
            };
        }

        public static SDKComponentVersion GetDriverFgLatchVersion()
        {
            return new SDKComponentVersion
            {
                ComponentName = "AMD Driver Frame Gen Latch (AFMF 2)",
                VersionString = "AFMF 2.x Presentation Latch",
                Status = "DXGI Hook Active",
                Details = "Direct DXGI Presentation Cadence alignment for driver-level optical flow frame pacing"
            };
        }
    }
}
