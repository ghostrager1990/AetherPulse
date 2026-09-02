using System;
using System.IO;
using System.Linq;

namespace AppUI.Services
{
    public class GameCapabilityInfo
    {
        public bool HasOptiScaler { get; set; }
        public bool HasDxvk { get; set; }
        public bool HasDirectX12 { get; set; }
        public bool HasDirectX11 { get; set; }
        public bool HasVulkan { get; set; }
        public bool HasNativeAntiLag2 { get; set; }
        public string AntiCheatName { get; set; } = string.Empty;
        public string BadgeText { get; set; } = string.Empty;
    }

    public static class GameInspectionService
    {
        public static GameCapabilityInfo InspectGame(string installDirectory)
        {
            var info = new GameCapabilityInfo();
            if (string.IsNullOrWhiteSpace(installDirectory) || !Directory.Exists(installDirectory))
            {
                return info;
            }

            try
            {
                // Inspect Anti-Lag 2 Native presence
                info.HasNativeAntiLag2 = File.Exists(Path.Combine(installDirectory, "amd_antilag2_dx12.dll")) ||
                                         File.Exists(Path.Combine(installDirectory, "amd_antilag2.dll")) ||
                                         Directory.GetFiles(installDirectory, "*antilag*", SearchOption.TopDirectoryOnly).Length > 0;

                // Inspect OptiScaler / DXVK / Wrappers
                info.HasOptiScaler = File.Exists(Path.Combine(installDirectory, "OptiScaler.ini")) ||
                                     File.Exists(Path.Combine(installDirectory, "nvngx.dll"));

                info.HasDxvk = File.Exists(Path.Combine(installDirectory, "dxvk.conf")) ||
                               File.Exists(Path.Combine(installDirectory, "d3d11.dll"));

                // Inspect Render Backends
                info.HasDirectX12 = File.Exists(Path.Combine(installDirectory, "d3d12.dll")) ||
                                    Directory.GetFiles(installDirectory, "*.exe", SearchOption.TopDirectoryOnly).Length > 0;

                if (info.HasOptiScaler)
                {
                    info.BadgeText = "OptiScaler Chain";
                }
                else if (info.HasNativeAntiLag2)
                {
                    info.BadgeText = "Anti-Lag 2 Native";
                }
                else if (info.HasDxvk)
                {
                    info.BadgeText = "DXVK Active";
                }
            }
            catch { }

            return info;
        }
    }
}