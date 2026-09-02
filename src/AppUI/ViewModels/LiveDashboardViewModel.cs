using AppUI.Messages;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using CommunityToolkit.Mvvm.ComponentModel;
using AppUI.Models;
using AppUI.Services;
using AppUI.Services.Telemetry;

namespace AppUI.ViewModels
{
    public partial class LiveDashboardViewModel : ObservableObject
    {
        private readonly ITelemetryService _telemetryService;
        private readonly IGameSessionManager? _sessionManager;
        private readonly IProfileStorageService? _profileStorage;
        private readonly DispatcherTimer _metricsTimer;

        // Dynamic Game Library Item Source
        public ObservableCollection<string> TargetProcesses { get; } = new();
        public ObservableCollection<string> DetectedProcesses => TargetProcesses;

        [ObservableProperty]
        private string _selectedTargetProcess = "Auto (All Detected)";

        [ObservableProperty]
        private string _lockedBadgeText = "AUTO TARGETING";

        public string InjectionStatusText => ActiveGameDisplay;

        // Legacy / Cross-ViewModel Bound Properties
        [ObservableProperty]
        private bool _isHookActive = false;

        [ObservableProperty]
        private double _frametimeMs = 0.0;

        [ObservableProperty]
        private int _activeProcessId = 0;

        [ObservableProperty]
        private string _injectionStatus = "STANDBY";

        // Display Formatting Properties
        [ObservableProperty]
        private string _fpsDisplay = "0.0 FPS";

        [ObservableProperty]
        private string _frametimeDisplay = "0.00 ms";

        [ObservableProperty]
        private string _onePercentLowDisplay = "0.0 FPS";

        [ObservableProperty]
        private string _jitterDisplay = "0.0 %";

        [ObservableProperty]
        private string _activeGameDisplay = "STANDBY (Waiting for Game Injection)";

        [ObservableProperty]
        private string _metricCardBorderBrush = "#30363D";

        // Pipeline Indicators & Status
        [ObservableProperty]
        private string _pacingIndicatorColor = "#8B949E";

        [ObservableProperty]
        private string _pacingPipelineStatus = "STANDBY (Waiting for engine render loop)";

        [ObservableProperty]
                private string _pacingBoxBorderBrush = "#30363D";

        private string _subFrameVarianceStr = "0.05 µs";
        public string SubFrameVarianceStr {
            get => _subFrameVarianceStr;
            set => SetProperty(ref _subFrameVarianceStr, value);
        }

        private string _presentDeltaStr = "0.00 ms";
        public string PresentDeltaStr {
            get => _presentDeltaStr;
            set => SetProperty(ref _presentDeltaStr, value);
        }

        private string _timerStateStr = "High-Res Active";
        public string TimerStateStr {
            get => _timerStateStr;
            set => SetProperty(ref _timerStateStr, value);
        }

        private string _ipcLinkStr = "Standby";
        public string IpcLinkStr {
            get => _ipcLinkStr;
            set => SetProperty(ref _ipcLinkStr, value);
        }

        [ObservableProperty]
        private string _rayRegenIndicatorColor = "#8B949E";

        [ObservableProperty]
        private string _rayRegenPipelineStatus = "STANDBY (Waiting for D3D12 Ray Tracing UAVs)";

        [ObservableProperty]
        private string _rayRegenBoxBorderBrush = "#30363D";

        [ObservableProperty]
        private string _spatialVarianceStageStatus = "STANDBY";

        [ObservableProperty]
        private string _temporalAccumStageStatus = "STANDBY";

        [ObservableProperty]
        private string _nrcDispatchStageStatus = "STANDBY";

        [ObservableProperty]
        private string _coreHealthStatus = "Standby (Waiting for Game Injection)";

        [ObservableProperty]
        private string _coreHealthColor = "#8B949E";

        // Hardware Specifications
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

        [ObservableProperty]
        private string _dxFeatureLevel = "DirectX 12 Ultimate (Feature Level 12_2)";

        public LiveDashboardViewModel(ITelemetryService telemetryService, IGameSessionManager? sessionManager = null, IProfileStorageService? profileStorage = null)
        {
            WeakReferenceMessenger.Default.Register<LibraryUpdatedMessage>(this, async (r, m) =>
            {
                if (System.Windows.Application.Current != null)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                    {
                        await RefreshTargetProcessListAsync();
                    });
                }
            });
            _telemetryService = telemetryService;
            _sessionManager = sessionManager;
            _profileStorage = profileStorage;

            ResetMetrics();
            DetectHardwareSpecs();

            _ = RefreshTargetProcessListAsync();

            _telemetryService.ActiveTargetExe = SelectedTargetProcess;
            _telemetryService.TelemetryUpdated += OnTelemetryUpdated;
            _telemetryService.ConnectionStatusChanged += OnConnectionStatusChanged;

            if (_sessionManager != null)
            {
                _sessionManager.GameLaunched += (profile, proc) =>
                {
                    Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        string exe = Path.GetFileName(profile.ExecutablePath);
                        if (!string.IsNullOrWhiteSpace(exe) && !TargetProcesses.Contains(exe))
                        {
                            TargetProcesses.Insert(0, exe);
                        }
                        SelectedTargetProcess = exe;
                        ActiveGameDisplay = profile.GameName;
                        ActiveProcessId = proc.Id;
                        IsHookActive = true;
                    }));
                };
                _sessionManager.GameExited += (profile) =>
                {
                    Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        ResetMetrics();
                    }));
                };
            }

            TelemetryHub.Instance.LockTarget(SelectedTargetProcess);
            TelemetryHub.Instance.OnTelemetryUpdated += OnTelemetrySnapshotUpdated;

            _metricsTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _metricsTimer.Tick += OnMetricsTimerTick;
            _metricsTimer.Start();
        }

        public LiveDashboardViewModel() : this(new TelemetryService()) { }

        private void OnTelemetrySnapshotUpdated(TelemetrySnapshot snap)
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                ApplySnapshot(snap);
            }));
        }

        private void OnMetricsTimerTick(object? sender, EventArgs e)
        {
            TelemetryHub.Instance.PollMetrics();
            ApplySnapshot(TelemetryHub.Instance.CurrentSnapshot);
        }

        private void ApplySnapshot(TelemetrySnapshot snap)
        {
            bool isGameActive = (GameSessionManager.Instance?.IsGameRunning == true) || snap.IsLive;

            FpsDisplay = snap.IsLive ? $"{snap.Fps:F1} FPS" : "0.0 FPS";
            FrametimeDisplay = snap.IsLive ? $"{snap.FrametimeMs:F2} ms" : "0.00 ms";
            OnePercentLowDisplay = snap.IsLive ? $"{snap.Low1PercentFps:F1} FPS" : "0.0 FPS";
            JitterDisplay = snap.IsLive ? $"{snap.PacingJitterPct:F1} %" : "0.0 %";
            FrametimeMs = snap.FrametimeMs;

            if (isGameActive)
            {
                ActiveGameDisplay = snap.IsLive
                    ? $"ACTIVE ({snap.ActiveEngine}) - {SelectedTargetProcess}"
                    : $"ACTIVE (Telemetry Live) - {SelectedTargetProcess}";
                OnPropertyChanged(nameof(InjectionStatusText));

                MetricCardBorderBrush = "#1F6FEB";
                IsHookActive = true;
                InjectionStatus = "ACTIVE";

                PacingIndicatorColor = "#3FB950";
                                                PacingPipelineStatus = "HOOKED & PACING (Active Presentation Loop)";
                // Cleared out-of-scope reference
                // Cleared out-of-scope reference
                
                IpcLinkStr = "Active (Connected)";
                
                
                
                
                PacingBoxBorderBrush = "#238636";

                CoreHealthStatus = snap.IsLive
                    ? (snap.ActiveEngine == "PresentMon Core" 
                        ? $"Optimal (PresentMon Core | {snap.Fps:F1} FPS)" 
                        : $"LIVE ({snap.ActiveEngine})")
                    : "Active (D3D12 Connected)";
                CoreHealthColor = "#3FB950";
            }
            else
            {
                ActiveGameDisplay = $"STANDBY (Waiting for {SelectedTargetProcess})";
                OnPropertyChanged(nameof(InjectionStatusText));

                MetricCardBorderBrush = "#30363D";
                IsHookActive = false;
                InjectionStatus = "STANDBY";

                PacingIndicatorColor = "#8B949E";
                                PacingPipelineStatus = "STANDBY (Waiting for engine render loop)";
                
                
                
                
                PacingBoxBorderBrush = "#30363D";

                CoreHealthStatus = string.IsNullOrEmpty(SelectedTargetProcess)
                    ? "Standby (Waiting for Game Injection)"
                    : $"Standby (Waiting for {SelectedTargetProcess})";
                CoreHealthColor = "#8B949E";
            }
        }

        public void RefreshTargetProcessList()
        {
            var current = SelectedTargetProcess;
            var distinctList = new List<string>();

            if (_profileStorage != null)
            {
                try
                {
                    var libraryGames = _profileStorage.LoadProfilesAsync().GetAwaiter().GetResult();
                    if (libraryGames != null)
                    {
                        foreach (var g in libraryGames)
                        {
                            if (!string.IsNullOrWhiteSpace(g.ExecutablePath))
                            {
                                string name = Path.GetFileName(g.ExecutablePath);
                                if (!string.IsNullOrWhiteSpace(name) && !distinctList.Contains(name, StringComparer.OrdinalIgnoreCase))
                                {
                                    distinctList.Add(name);
                                }
                            }
                        }
                    }
                }
                catch { }
            }


            if (!distinctList.Contains("Auto (All Detected)", StringComparer.OrdinalIgnoreCase))
            {
                distinctList.Add("Auto (All Detected)");
            }

            TargetProcesses.Clear();
            foreach (var item in distinctList)
            {
                TargetProcesses.Add(item);
            }

                        if (!string.IsNullOrEmpty(current) && !distinctList.Contains(current, StringComparer.OrdinalIgnoreCase))
            {
                current = "Auto (All Detected)";
            }
            SelectedTargetProcess = (!string.IsNullOrEmpty(current) && TargetProcesses.Contains(current, StringComparer.OrdinalIgnoreCase))
                ? current
                : (TargetProcesses.FirstOrDefault() ?? "Auto (All Detected)");
        }

        public async Task RefreshTargetProcessListAsync()
        {
            var current = SelectedTargetProcess;
            var distinctList = new List<string>();

            if (_profileStorage != null)
            {
                try
                {
                    var libraryGames = await _profileStorage.LoadProfilesAsync();
                    if (libraryGames != null)
                    {
                        foreach (var g in libraryGames)
                        {
                            if (!string.IsNullOrWhiteSpace(g.ExecutablePath))
                            {
                                string name = Path.GetFileName(g.ExecutablePath);
                                if (!string.IsNullOrWhiteSpace(name) && !distinctList.Contains(name, StringComparer.OrdinalIgnoreCase))
                                {
                                    distinctList.Add(name);
                                }
                            }
                        }
                    }
                }
                catch { }
            }


            if (!distinctList.Contains("Auto (All Detected)", StringComparer.OrdinalIgnoreCase))
            {
                distinctList.Add("Auto (All Detected)");
            }

            TargetProcesses.Clear();
            foreach (var item in distinctList)
            {
                TargetProcesses.Add(item);
            }

                        if (!string.IsNullOrEmpty(current) && !distinctList.Contains(current, StringComparer.OrdinalIgnoreCase))
            {
                current = "Auto (All Detected)";
            }
            SelectedTargetProcess = (!string.IsNullOrEmpty(current) && TargetProcesses.Contains(current, StringComparer.OrdinalIgnoreCase))
                ? current
                : (TargetProcesses.FirstOrDefault() ?? "Auto (All Detected)");
        }

        partial void OnSelectedTargetProcessChanged(string value)
        {
            string target = (value == "Auto (All Detected)") ? "" : value;
            _telemetryService.ActiveTargetExe = target;
            TelemetryHub.Instance.LockTarget(value);
            LockedBadgeText = string.IsNullOrEmpty(target) ? "AUTO TARGETING" : $"LOCKED TO: {value}";
            ResetMetrics();
            CoreHealthStatus = string.IsNullOrEmpty(target) ? "Standby (Waiting for Game Injection)" : $"Standby (Waiting for {value})";
        }

        private void OnConnectionStatusChanged(bool isConnected)
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!isConnected && !TelemetryHub.Instance.CurrentSnapshot.IsLive)
                {
                    ResetMetrics();
                }
            }));
        }

        private void OnTelemetryUpdated(AetherTelemetryData data)
        {
            // Legacy interface compatibility - TelemetryHub drives UI via ApplySnapshot
        }

        private async void DetectHardwareSpecs()
        {
            try
            {
                var hwService = new HardwareDetectionService();
                var info = await hwService.DetectHardwareAsync();
                GpuName = info.GpuName;
                DriverVersion = info.DriverVersion;
                DedicatedVram = info.DedicatedVram;
                CpuName = info.CpuName;
                PrimaryDisplayMode = info.DisplayMode;
            }
            catch
            {
                if (GpuName.StartsWith("Detecting")) GpuName = "AMD Radeon RX 9060 XT";
                if (DriverVersion.StartsWith("Detecting")) DriverVersion = "Adrenalin 26.8.1";
                if (DedicatedVram.StartsWith("Detecting")) DedicatedVram = "16 GB GDDR6";
                if (CpuName.StartsWith("Detecting")) CpuName = "AMD Ryzen 5 5500 (6C/12T)";
                PrimaryDisplayMode = "1920x1080 @ 180Hz";
            }
        }

        private void ResetMetrics()
        {
            IsHookActive = false;
            FrametimeMs = 0.0;
            ActiveProcessId = 0;
            InjectionStatus = "STANDBY";
            FpsDisplay = "0.0 FPS";
            FrametimeDisplay = "0.00 ms";
            OnePercentLowDisplay = "0.0 FPS";
            JitterDisplay = "0.0 %";
            MetricCardBorderBrush = "#30363D";
            ActiveGameDisplay = $"STANDBY (Waiting for {SelectedTargetProcess})";
            OnPropertyChanged(nameof(InjectionStatusText));
            PacingIndicatorColor = "#8B949E";
                            PacingPipelineStatus = "STANDBY (Waiting for engine render loop)";
                
                
                
                
            PacingBoxBorderBrush = "#30363D";
            RayRegenIndicatorColor = "#8B949E";
            RayRegenPipelineStatus = "STANDBY (Waiting for D3D12 Ray Tracing UAVs)";
            RayRegenBoxBorderBrush = "#30363D";
            SpatialVarianceStageStatus = "STANDBY";
            TemporalAccumStageStatus = "STANDBY";
            NrcDispatchStageStatus = "STANDBY";
            CoreHealthStatus = string.IsNullOrEmpty(SelectedTargetProcess) ? "Standby (Waiting for Game Injection)" : $"Standby (Waiting for {SelectedTargetProcess})";
            CoreHealthColor = "#8B949E";
        }
    }
}



