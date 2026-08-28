using System;
using System.IO;
using System.Linq;

namespace AppUI.Services
{
    public enum RecommendedProxyType
    {
        StreamlineInterposer,
        VersionDll,
        DxgiDll
    }

    public class GameCapabilityInfo
    {
        public RecommendedProxyType RecommendedType { get; set; } = RecommendedProxyType.VersionDll;
        public string BadgeText { get; set; } = string.Empty;
        public string RecommendationReason { get; set; } = string.Empty;
        public bool HasStreamline { get; set; }
        public bool HasReShade { get; set; }
    }

    public static class GameInspectionService
    {
        public static GameCapabilityInfo InspectGame(string? gameDirectory)
        {
            var info = new GameCapabilityInfo();

            if (string.IsNullOrWhiteSpace(gameDirectory) || !Directory.Exists(gameDirectory))
            {
                info.RecommendedType = RecommendedProxyType.VersionDll;
                info.BadgeText = "🛡️ Standard D3D12/Vulkan (Pacer Mode)";
                info.RecommendationReason = "Target directory not detected. Defaulting to version.dll presentation pacer.";
                return info;
            }

            try
            {
                var files = Directory.GetFiles(gameDirectory, "*.*", SearchOption.TopDirectoryOnly);

                bool hasSl = files.Any(f => Path.GetFileName(f).Equals("sl.interposer.dll", StringComparison.OrdinalIgnoreCase) ||
                                           Path.GetFileName(f).Equals("sl.common.dll", StringComparison.OrdinalIgnoreCase) ||
                                           Path.GetFileName(f).Equals("sl.dlss.dll", StringComparison.OrdinalIgnoreCase) ||
                                           Path.GetFileName(f).Equals("sl.dlss_d.dll", StringComparison.OrdinalIgnoreCase) ||
                                           Path.GetFileName(f).Equals("sl.dlss_g.dll", StringComparison.OrdinalIgnoreCase));

                if (!hasSl)
                {
                    try
                    {
                        var subDirs = Directory.GetDirectories(gameDirectory);
                        foreach (var sub in subDirs)
                        {
                            var subFiles = Directory.GetFiles(sub, "*.*", SearchOption.TopDirectoryOnly);
                            if (subFiles.Any(f => Path.GetFileName(f).Equals("sl.interposer.dll", StringComparison.OrdinalIgnoreCase) ||
                                                 Path.GetFileName(f).Equals("sl.common.dll", StringComparison.OrdinalIgnoreCase) ||
                                                 Path.GetFileName(f).Equals("sl.dlss.dll", StringComparison.OrdinalIgnoreCase)))
                            {
                                hasSl = true;
                                break;
                            }
                        }
                    }
                    catch { }
                }

                bool hasReshade = files.Any(f => Path.GetFileName(f).Equals("dxgi.dll", StringComparison.OrdinalIgnoreCase) ||
                                                Path.GetFileName(f).Equals("ReShade.ini", StringComparison.OrdinalIgnoreCase) ||
                                                Path.GetFileName(f).Equals("ReShade64.dll", StringComparison.OrdinalIgnoreCase));

                info.HasStreamline = hasSl;
                info.HasReShade = hasReshade;

                if (hasSl)
                {
                    info.RecommendedType = RecommendedProxyType.VersionDll;
                    info.BadgeText = "✨ FSR Ray Regeneration + Multi-Frame Ready";
                    info.RecommendationReason = "This title supports D3D12 Ray Tracing. Custom proxy injection (version.dll) provides active SwapChain frame cadence alignment, half-interval pacing, and experimental FidelityFX Ray Regeneration interop.";
                }
                else
                {
                    info.RecommendedType = RecommendedProxyType.VersionDll;
                    info.BadgeText = "✨ Universal D3D12 (Pacer & Multi-Frame Ready)";
                    info.RecommendationReason = "This title uses standard DirectX 12. Custom proxy injection (version.dll) provides frame pacing and latency metering.";
                }
            }
            catch (Exception ex)
            {
                info.RecommendedType = RecommendedProxyType.VersionDll;
                info.BadgeText = "🛡️ Standard D3D12/Vulkan (Pacer Mode)";
                info.RecommendationReason = $"Inspection error: {ex.Message}. Defaulting to version.dll.";
            }

            return info;
        }
    }
}
