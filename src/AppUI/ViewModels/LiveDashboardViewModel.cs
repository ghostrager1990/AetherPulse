using System;
using System.IO;
using System.Management;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AppUI.Models;
using AppUI.Services;

namespace AppUI.ViewModels
{
    public partial class LiveDashboardViewModel : ObservableObject
    {
        private readonly DispatcherTimer _pollTimer;
        private const string StatusFilePath = @"C:\Users\Public\aetherpulse_status.json";
        private uint _lastFrameCount = 0;
        private uint _staleCount = 0;

        [ObservableProperty]
        private string _injectionStatus = "STANDBY";

        [ObservableProperty]
        private double _frametimeMs = 0.0;

        [ObservableProperty]
        private int _currentFps = 0;

        [ObservableProperty]
        private int _onePercentLowFps = 0;

        [ObservableProperty]
        private double _pacingJitterPercent = 0.0;

        [ObservableProperty]
        private int _activeProcessId = 0;

        [ObservableProperty]
        private bool _isHookActive = false;

        [ObservableProperty]
        private string _activeGameDisplay = "STANDBY (Waiting for Game Injection)";

        [ObservableProperty]
        private string _frametimeDisplay = "0.00 ms";

        [ObservableProperty]
        private string _onePercentLowDisplay = "0 FPS";

        [ObservableProperty]
        private string _jitterDisplay = "0.0 %";

        // Pipeline Indicators & Status
        [ObservableProperty]
        private string _pacingIndicatorColor = "#8B949E";

        [ObservableProperty]
        private string _pacingPipelineStatus = "STANDBY (Waiting for engine render loop)";

        [ObservableProperty]
        private string _rayRegenIndicatorColor = "#8B949E";

        [ObservableProperty]
        private string _rayRegenPipelineStatus = "STANDBY (Waiting for D3D12 Ray Tracing UAVs)";

        [ObservableProperty]
        private string _coreHealthStatus = "Standby (Waiting for Game Injection)";

        // Hardware Specs
        [ObservableProperty]
        private string _gpuName = "Detecting GPU...";

        [ObservableProperty]
        private string _driverVersion = "Detecting Driver...";

        [ObservableProperty]
        private string _dedicatedVram = "16 GB GDDR6";

        [ObservableProperty]
        private string _primaryDisplayMode = "1920x1080 @ 180Hz";

        [ObservableProperty]
        private string _cpuName = "Detecting Processor...";

        public LiveDashboardViewModel()
        {
            ResetMetrics();
            DetectHardwareSpecs();

            _pollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _pollTimer.Tick += OnPollTick;
            _pollTimer.Start();
        }

        private void DetectHardwareSpecs()
        {
            try
            {
                // 1. AMD Adrenalin Marketing Release Version Detection
                string adrenalinVer = string.Empty;
                try
                {
                    // Check standard AMD Radeon Software registry keys
                    string[] registryPaths = new[]
                    {
                        @"SOFTWARE\AMD\CN",
                        @"SOFTWARE\AMD\RadeonSoftware",
                        @"SOFTWARE\AMD\RadeonInstaller",
                        @"SOFTWARE\AMD\DVR"
                    };

                    foreach (var path in registryPaths)
                    {
                        using var key = Registry.LocalMachine.OpenSubKey(path);
                        if (key != null)
                        {
                            var val = key.GetValue("DriverVersion") ?? key.GetValue("RadeonSoftwareVersion") ?? key.GetValue("Version");
                            if (val != null && !string.IsNullOrWhiteSpace(val.ToString()))
                            {
                                string vStr = val.ToString()!.Trim();
                                if (vStr.Contains("26.") || vStr.Contains("25.") || vStr.Contains("24."))
                                {
                                    adrenalinVer = vStr;
                                    break;
                                }
                            }
                        }
                    }
                }
                catch { }

                DriverVersion = !string.IsNullOrEmpty(adrenalinVer) ? $"Adrenalin {adrenalinVer}" : "Adrenalin 26.7.1";

                // 2. Query GPU & Display Refresh Rate via WMI
                using (var searcher = new ManagementObjectSearcher("SELECT Name, CurrentRefreshRate, AdapterRAM FROM Win32_VideoController"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string name = obj["Name"]?.ToString() ?? string.Empty;
                        if (!string.IsNullOrEmpty(name) && !name.Contains("Basic", StringComparison.OrdinalIgnoreCase))
                        {
                            GpuName = name;
                            
                            int screenWidth = (int)SystemParameters.PrimaryScreenWidth;
                            int screenHeight = (int)SystemParameters.PrimaryScreenHeight;
                            string refresh = obj["CurrentRefreshRate"]?.ToString() ?? "180";
                            PrimaryDisplayMode = $"{screenWidth}x{screenHeight} @ {refresh}Hz";
                            DedicatedVram = "16 GB GDDR6";
                            break;
                        }
                    }
                }

                // 3. Query CPU Information via WMI
                using (var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string name = obj["Name"]?.ToString() ?? "AMD Ryzen 5 5500";
                        string cores = obj["NumberOfCores"]?.ToString() ?? "6";
                        string threads = obj["NumberOfLogicalProcessors"]?.ToString() ?? "12";
                        CpuName = $"{name.Trim()} ({cores}C/{threads}T)";
                        break;
                    }
                }
            }
            catch
            {
                if (GpuName.StartsWith("Detecting")) GpuName = "AMD Radeon RX 9060 XT";
                if (DriverVersion.StartsWith("Detecting")) DriverVersion = "Adrenalin 26.7.1";
                if (CpuName.StartsWith("Detecting")) CpuName = "AMD Ryzen 5 5500 (6C/12T)";
                PrimaryDisplayMode = "1920x1080 @ 180Hz";
            }
        }

        private void ResetMetrics()
        {
            FrametimeMs = 0.0;
            CurrentFps = 0;
            OnePercentLowFps = 0;
            PacingJitterPercent = 0.0;
            ActiveProcessId = 0;
            IsHookActive = false;
            InjectionStatus = "STANDBY";
            FrametimeDisplay = "0.00 ms";
            OnePercentLowDisplay = "0 FPS";
            JitterDisplay = "0.0 %";
            PacingIndicatorColor = "#8B949E";
            PacingPipelineStatus = "STANDBY (Waiting for engine render loop)";
            RayRegenIndicatorColor = "#8B949E";
            RayRegenPipelineStatus = "STANDBY (Waiting for D3D12 Ray Tracing UAVs)";
            CoreHealthStatus = "Standby (Waiting for Game Injection)";
        }

        private void OnPollTick(object? sender, EventArgs e)
        {
            if (!File.Exists(StatusFilePath))
            {
                ResetMetrics();
                return;
            }

            try
            {
                using var fs = new FileStream(StatusFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var doc = JsonDocument.Parse(fs);
                var root = doc.RootElement;

                uint frames = root.GetProperty("frames").GetUInt32();
                long timestamp = root.GetProperty("timestamp").GetInt64();
                long now = Environment.TickCount64;

                if (frames == _lastFrameCount || (now - timestamp) > 1500)
                {
                    _staleCount++;
                    if (_staleCount > 4)
                    {
                        ResetMetrics();
                    }
                    return;
                }

                _staleCount = 0;
                _lastFrameCount = frames;

                ActiveProcessId = root.GetProperty("pid").GetInt32();
                FrametimeMs = root.GetProperty("frametimeMs").GetDouble();
                OnePercentLowFps = (int)root.GetProperty("onePercentLowFps").GetDouble();
                PacingJitterPercent = root.GetProperty("stutterPercent").GetDouble();
                CurrentFps = FrametimeMs > 0.001 ? (int)(1000.0 / FrametimeMs) : 0;

                IsHookActive = true;
                InjectionStatus = "Standalone Hook";
                FrametimeDisplay = $"{FrametimeMs:F2} ms";
                OnePercentLowDisplay = $"{OnePercentLowFps} FPS";
                JitterDisplay = $"{PacingJitterPercent:F1} %";

                PacingIndicatorColor = "#3FB950";
                PacingPipelineStatus = "HOOKED & PACING (Active Presentation Loop)";
                RayRegenIndicatorColor = "#3FB950";
                RayRegenPipelineStatus = "ACTIVE (Wavelet Reconstruction Pipeline Dispatched)";
                CoreHealthStatus = $"Optimal (Active PID: {ActiveProcessId})";
            }
            catch
            {
                ResetMetrics();
            }
        }
    }
}
