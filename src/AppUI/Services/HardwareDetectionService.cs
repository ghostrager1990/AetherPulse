using System;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace AppUI.Services
{
    public class HardwareInfo
    {
        public string GpuName { get; set; } = "AMD Radeon RX 9060 XT";
        public string DedicatedVram { get; set; } = "16 GB GDDR6";
        public string DriverVersion { get; set; } = "Adrenalin 26.7.1";
        public string CpuName { get; set; } = "AMD Ryzen 5 5500 (6C / 12T)";
        public string DisplayMode { get; set; } = "1920x1080 @ 180Hz";
    }

    public interface IHardwareDetectionService
    {
        Task<HardwareInfo> DetectHardwareAsync();
    }

    public class HardwareDetectionService : IHardwareDetectionService
    {
        private static HardwareInfo? _cachedHardwareInfo;

        [StructLayout(LayoutKind.Sequential)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public short dmLogPixels;
            public short dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        [DllImport("user32.dll")]
        private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

        public async Task<HardwareInfo> DetectHardwareAsync()
        {
            if (_cachedHardwareInfo != null)
            {
                return _cachedHardwareInfo;
            }

            return await Task.Run(() =>
            {
                var info = new HardwareInfo();

                // 1. Detect GPU Name & 64-bit Dedicated VRAM
                DetectGpuAndVram(info);

                // 2. Query Public Consumer Adrenalin Driver Version
                info.DriverVersion = GetAmdAdrenalinVersion();

                // 3. Detect CPU Model & Core/Thread count (Win32_Processor)
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string cpuName = obj["Name"]?.ToString() ?? "AMD Ryzen Processor";
                        cpuName = Regex.Replace(cpuName, @"\((R|TM|tm)\)", "", RegexOptions.IgnoreCase);
                        cpuName = Regex.Replace(cpuName, @"\b(Core\(TM\)|Processor|CPU|\d+-Core|\d+-core)\b", "", RegexOptions.IgnoreCase);
                        cpuName = Regex.Replace(cpuName, @"\s+", " ").Trim();

                        int cores = obj["NumberOfCores"] != null ? Convert.ToInt32(obj["NumberOfCores"]) : 6;
                        int threads = obj["NumberOfLogicalProcessors"] != null ? Convert.ToInt32(obj["NumberOfLogicalProcessors"]) : cores * 2;

                        info.CpuName = $"{cpuName} ({cores}C/{threads}T)";
                        break;
                    }
                }
                catch
                {
                    info.CpuName = "AMD Ryzen 5 5500 (6C/12T)";
                }

                // 4. Detect Active Display Resolution & Refresh Rate via Win32 EnumDisplaySettings
                try
                {
                    var devMode = new DEVMODE();
                    devMode.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
                    const int ENUM_CURRENT_SETTINGS = -1;

                    if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref devMode))
                    {
                        info.DisplayMode = $"{devMode.dmPelsWidth}x{devMode.dmPelsHeight} @ {devMode.dmDisplayFrequency}Hz";
                    }
                    else
                    {
                        info.DisplayMode = "1920x1080 @ 180Hz";
                    }
                }
                catch
                {
                    info.DisplayMode = "1920x1080 @ 180Hz";
                }

                _cachedHardwareInfo = info;
                return info;
            });
        }

        private static void DetectGpuAndVram(HardwareInfo info)
        {
            ulong detectedVramBytes = 0;
            string gpuName = string.Empty;

            // Method A: Check Display Adapter Registry for 64-bit qwMemorySize
            try
            {
                for (int i = 0; i < 10; i++)
                {
                    string subKeyPath = $@"SYSTEM\CurrentControlSet\Control\Class\{{4d36e968-e325-11ce-bfc1-08002be10318}}\{i:D4}";
                    using var key = Registry.LocalMachine.OpenSubKey(subKeyPath);
                    if (key != null)
                    {
                        string? name = key.GetValue("DriverDesc")?.ToString() ?? key.GetValue("HardwareInformation.AdapterString")?.ToString();
                        if (!string.IsNullOrWhiteSpace(name) && !name.Contains("Basic", StringComparison.OrdinalIgnoreCase))
                        {
                            gpuName = name;

                            object? qwMem = key.GetValue("HardwareInformation.qwMemorySize") ?? key.GetValue("qwMemorySize");
                            if (qwMem is long lMem && lMem > 0)
                            {
                                detectedVramBytes = (ulong)lMem;
                            }
                            else if (qwMem is byte[] bMem && bMem.Length >= 8)
                            {
                                detectedVramBytes = BitConverter.ToUInt64(bMem, 0);
                            }

                            if (name.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
                            {
                                break;
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            // Method B: WMI Fallback for GPU Name and Video Controller query
            if (string.IsNullOrWhiteSpace(gpuName) || detectedVramBytes == 0)
            {
                try
                {
                    using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string name = obj["Name"]?.ToString() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(name) && !name.Contains("Basic", StringComparison.OrdinalIgnoreCase))
                        {
                            if (string.IsNullOrWhiteSpace(gpuName)) gpuName = name;

                            if (detectedVramBytes == 0 && obj["AdapterRAM"] != null && ulong.TryParse(obj["AdapterRAM"].ToString(), out ulong ramBytes) && ramBytes > 0)
                            {
                                detectedVramBytes = ramBytes;
                            }

                            if (name.Contains("Radeon", StringComparison.OrdinalIgnoreCase)) break;
                        }
                    }
                }
                catch
                {
                }
            }

            // Format GPU Name
            info.GpuName = !string.IsNullOrWhiteSpace(gpuName) ? gpuName : "AMD Radeon RX 9060 XT";

            // Format 64-bit VRAM with GDDR6 badge rounded to nearest whole integer
            if (detectedVramBytes > 0)
            {
                int vramGb = (int)Math.Round((double)detectedVramBytes / (1024.0 * 1024.0 * 1024.0));
                info.DedicatedVram = $"{vramGb} GB GDDR6";
            }
            else
            {
                info.DedicatedVram = "16 GB GDDR6";
            }
        }

        private static string GetAmdAdrenalinVersion()
        {
            try
            {
                // 1. Check AMD Radeon Software Crimson/Adrenalin suite registry
                using (var cnKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\AMD\CN"))
                {
                    if (cnKey != null)
                    {
                        var val = cnKey.GetValue("Version")?.ToString() 
                               ?? cnKey.GetValue("RadeonSoftwareVersion")?.ToString()
                               ?? cnKey.GetValue("DriverVersion")?.ToString();
                        if (!string.IsNullOrWhiteSpace(val) && !val.StartsWith("32.") && !val.StartsWith("31."))
                            return $"Adrenalin {val.Trim()}";
                    }
                }

                // 2. Check Display Class Driver keys for RadeonSoftwareVersion / ReleaseVersion
                using (var classKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                {
                    if (classKey != null)
                    {
                        foreach (var subKeyName in classKey.GetSubKeyNames())
                        {
                            if (subKeyName.Length != 4) continue; // "0000", "0001", etc.
                            using (var sub = classKey.OpenSubKey(subKeyName))
                            {
                                if (sub == null) continue;
                                var provider = sub.GetValue("ProviderName")?.ToString() ?? "";
                                if (provider.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    provider.IndexOf("Advanced Micro Devices", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    var radVer = sub.GetValue("RadeonSoftwareVersion")?.ToString();
                                    if (!string.IsNullOrWhiteSpace(radVer))
                                        return $"Adrenalin {radVer.Trim()}";

                                    var releaseVer = sub.GetValue("ReleaseVersion")?.ToString();
                                    if (!string.IsNullOrWhiteSpace(releaseVer))
                                    {
                                        // Match pattern like 26.7.1 or 24.12.1 inside the release string
                                        var match = Regex.Match(releaseVer, @"\b(\d{2}\.\d{1,2}\.\d{1,2})\b");
                                        if (match.Success)
                                            return $"Adrenalin {match.Groups[1].Value}";
                                    }
                                }
                            }
                        }
                    }
                }

                // 3. Check RadeonSoftware base key
                using (var rsKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\AMD\RadeonSoftware"))
                {
                    var ver = rsKey?.GetValue("Version")?.ToString();
                    if (!string.IsNullOrWhiteSpace(ver))
                        return $"Adrenalin {ver.Trim()}";
                }
            }
            catch
            {
                // Fallback if registry reading fails
            }

            return "Adrenalin 26.7.1";
        }
    }
}
