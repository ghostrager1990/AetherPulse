using System;
using Microsoft.Win32;
using System.Management;

namespace AppUI.Services
{
    public class GpuCapabilities
    {
        public string GpuName { get; set; } = "AMD Radeon RX 9060 XT";
        public double DedicatedVramGb { get; set; } = 16.0;
        public bool SupportsHardwareDxr { get; set; } = true;
        public bool IsRdnaArchitecture { get; set; } = true;
        public bool IsLowVramWarning => DedicatedVramGb < 10.0;
    }

    public static class GpuCapabilityService
    {
        private static GpuCapabilities? _cachedCapabilities;

        public static GpuCapabilities GetCapabilities()
        {
            if (_cachedCapabilities != null) return _cachedCapabilities;

            var caps = new GpuCapabilities();

            try
            {
                // Method 1: Read 64-bit qwMemorySize from Windows Display Driver Registry
                using (var videoKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                {
                    if (videoKey != null)
                    {
                        foreach (var subKeyName in videoKey.GetSubKeyNames())
                        {
                            if (subKeyName.StartsWith("000"))
                            {
                                using var driverKey = videoKey.OpenSubKey(subKeyName);
                                if (driverKey != null)
                                {
                                    var name = driverKey.GetValue("DriverDesc")?.ToString();
                                    var memObj = driverKey.GetValue("HardwareInformation.qwMemorySize");

                                    if (!string.IsNullOrWhiteSpace(name) && !name.Contains("Basic", StringComparison.OrdinalIgnoreCase))
                                    {
                                        caps.GpuName = name;
                                        if (memObj != null && long.TryParse(memObj.ToString(), out long bytes) && bytes > 0)
                                        {
                                            caps.DedicatedVramGb = Math.Round((double)bytes / (1024.0 * 1024.0 * 1024.0), 0);
                                        }
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }

                // Fallback sanity check
                if (caps.DedicatedVramGb < 4.0 || caps.DedicatedVramGb > 64.0)
                {
                    caps.DedicatedVramGb = 16.0;
                }

                caps.IsRdnaArchitecture = caps.GpuName.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
                                          caps.GpuName.Contains("RX", StringComparison.OrdinalIgnoreCase);
                caps.SupportsHardwareDxr = true;
            }
            catch
            {
                caps.GpuName = "AMD Radeon RX 9060 XT";
                caps.DedicatedVramGb = 16.0;
                caps.SupportsHardwareDxr = true;
                caps.IsRdnaArchitecture = true;
            }

            _cachedCapabilities = caps;
            return caps;
        }
    }
}
