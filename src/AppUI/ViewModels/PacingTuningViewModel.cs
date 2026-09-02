using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AppUI.Models;
using AppUI.Services.Pacing;

namespace AppUI.ViewModels
{
    public partial class PacingTuningViewModel : ObservableObject
    {
        [ObservableProperty]
        private GameProfile _activeProfile;

        [ObservableProperty]
        private ObservableCollection<GameProfile> _availableProfiles = new();

        [ObservableProperty]
        private int _selectedTargetFps = 180;

        [ObservableProperty]
        private bool _autoMatchFps = true;

        [ObservableProperty]
        private int _detectedDisplayRefreshRate = 180;

        [ObservableProperty]
        private string _displayPresetLabel = "180 FPS (Match Display)";

        public bool Is1xModeActive => ActiveProfile?.FrameGenMultiplier == "1X";
        public bool Is2xModeActive => ActiveProfile?.FrameGenMultiplier == "2X" || string.IsNullOrEmpty(ActiveProfile?.FrameGenMultiplier);
        public bool Is3xModeActive => ActiveProfile?.FrameGenMultiplier == "3X";
        public bool Is4xModeActive => ActiveProfile?.FrameGenMultiplier == "4X";
        public bool IsAdaptiveModeActive => ActiveProfile?.FrameGenMultiplier == "Adaptive";

        public bool IsFps0Active => SelectedTargetFps == 0;
        public bool IsFps60Active => SelectedTargetFps == 60;
        public bool IsFps90Active => SelectedTargetFps == 90;
        public bool IsFps120Active => SelectedTargetFps == 120;
        public bool IsFps144Active => SelectedTargetFps == 144;
        public bool IsFps180Active => SelectedTargetFps == 180 || SelectedTargetFps == DetectedDisplayRefreshRate;

        public string CadenceHandshakeReasoning
        {
            get
            {
                if (ActiveProfile == null) return "Cadence scheduler standing by.";
                return ActiveProfile.FrameGenMultiplier switch
                {
                    "1X" => "Handshake: Native 1X Mode - Direct presentation with zero sub-frame delay and instantaneous response.",
                    "2X" => "Handshake: 2X Mode active - Frame interval dynamically aligned to 50% midpoint (50:50) to eliminate judder.",
                    "3X" => "Handshake: 3X Mode active - Pacing engine dividing frame intervals into 33% & 66% sub-phases.",
                    "4X" => "Handshake: 4X Mode active - High-frequency quad dispatch (25%, 50%, 75%) sub-frame alignment.",
                    "Adaptive" => "Handshake: Adaptive Cadence Lock active - Dynamically tracking real vs interpolated intervals.",
                    _ => "Handshake: 2X Mode (50:50 cadence alignment) active."
                };
            }
        }

        public bool IsAutoEmaEnabled
        {
            get => ActiveProfile?.AutoEma ?? true;
            set
            {
                if (ActiveProfile != null && ActiveProfile.AutoEma != value)
                {
                    ActiveProfile.AutoEma = value;
                    OnPropertyChanged(nameof(IsAutoEmaEnabled));
                    OnPropertyChanged(nameof(IsManualEmaAllowed));
                    OnPropertyChanged(nameof(EmaSliderOpacity));
                    OnPropertyChanged(nameof(EmaAlphaDisplay));
                    OnPropertyChanged(nameof(SmoothingRecommendationText));
                    PushIpcConfig();
                }
            }
        }

        public bool IsManualEmaAllowed => !IsAutoEmaEnabled;
        public double EmaSliderOpacity => IsAutoEmaEnabled ? 0.45 : 1.0;

        public double LiveAdaptiveAlpha
        {
            get
            {
                var snap = AppUI.Services.Telemetry.TelemetryHub.Instance.CurrentSnapshot;
                double fps = snap.IsLive && snap.Fps > 0 ? snap.Fps : 60.0;
                if (fps <= 40.0) return 0.050;
                if (fps >= 144.0) return 0.220;
                double t = (fps - 40.0) / (144.0 - 40.0);
                return 0.050 + t * (0.220 - 0.050);
            }
        }

        public string EmaAlphaDisplay
        {
            get
            {
                if (IsAutoEmaEnabled)
                {
                    return $"AUTO ({LiveAdaptiveAlpha:F3})";
                }
                return ActiveProfile != null ? $"{ActiveProfile.EmaAlpha:F3}" : "0.050";
            }
        }

        public bool IsExternalLimiterDetected
        {
            get
            {
                var ipc = AppUI.Services.Pacing.PacingIpcService.Instance.ReadCurrentIPC();
                if (ipc.IsExternalLimiterActive == 1) return true;
                var snap = AppUI.Services.Telemetry.TelemetryHub.Instance.CurrentSnapshot;
                return snap.IsExternalLimiterActive;
            }
        }

        public string SmoothingRecommendationText
        {
            get
            {
                if (IsAutoEmaEnabled)
                {
                    return $"Auto-Scaling: {LiveAdaptiveAlpha:F3} (Dynamically scaled between 0.05 @ 40 FPS up to 0.22 @ 144+ FPS)";
                }
                if (ActiveProfile == null) return "0.050 (High-refresh stable curve)";
                float a = ActiveProfile.EmaAlpha;
                if (a <= 0.060f) return $"{a:F3} (Recommended for high-refresh 144Hz-240Hz stable curve)";
                if (a <= 0.120f) return $"{a:F3} (Balanced tracking response)";
                return $"{a:F3} (Fast reactive tracking for fluctuating frame rates)";
            }
        }

        public string QueueDepthRecommendationText
        {
            get
            {
                if (ActiveProfile == null) return "1 Frame - Lowest input latency (Direct GPU back-buffer flip)";
                return ActiveProfile.MaxFrameLatency switch
                {
                    1 => "1 Frame - Recommended for lowest input latency (FreeSync / G-Sync recommended)",
                    2 => "2 Frames - Balanced queue smoothing (Prevents pipeline stall on CPU bottlenecks)",
                    3 => "3 Frames - Maximum throughput (Smooths uneven frametimes at the expense of +16ms latency)",
                    _ => $"{ActiveProfile.MaxFrameLatency} Frames"
                };
            }
        }

        public string PrecisionTimerRecommendationText
        {
            get
            {
                if (ActiveProfile == null) return "250 µs - Recommended balance between CPU overhead and exact millisecond VBlank flips";
                uint t = ActiveProfile.SpinYieldMicroseconds;
                if (t <= 150) return $"{t} µs - Aggressive micro-sleep (Best for high-core CPUs, eliminates micro-jitter)";
                if (t <= 400) return $"{t} µs - Recommended (Optimal microsecond spin threshold before flip)";
                return $"{t} µs - Relaxed timer (Lower CPU usage, higher scheduling tolerance)";
            }
        }

        public PacingTuningViewModel()
        {
            var initialProfile = new GameProfile
            {
                Name = "Global Default Profile",
                PacingEnabled = true,
                HalfIntervalCadenceEnabled = true,
                TargetFpsCap = 180,
                EmaAlpha = 0.050f,
                ForceFlipDiscard = true,
                MaxFrameLatency = 1,
                SpinYieldMicroseconds = 250,
                FrameGenMultiplier = "2X"
            };

            _activeProfile = initialProfile;
            _selectedTargetFps = initialProfile.TargetFpsCap;
            HookProfileEvents(_activeProfile);

            AppUI.Services.Telemetry.TelemetryHub.Instance.OnTelemetryUpdated += (snap) =>
            {
                OnPropertyChanged(nameof(IsExternalLimiterDetected));
            };
        }

        public PacingTuningViewModel(object? p1) : this() { }
        public PacingTuningViewModel(object? p1, object? p2) : this() { }
        public PacingTuningViewModel(object? p1, object? p2, object? p3) : this() { }

        public void SetProfile(GameProfile? profile)
        {
            if (profile != null)
            {
                UnhookProfileEvents(ActiveProfile);
                ActiveProfile = profile;
                SelectedTargetFps = profile.TargetFpsCap;
                HookProfileEvents(ActiveProfile);

                NotifyAllProperties();
            }
        }

        private void HookProfileEvents(GameProfile? profile)
        {
            if (profile != null)
            {
                profile.PropertyChanged += OnProfilePropertyChanged;
            }
        }

        private void UnhookProfileEvents(GameProfile? profile)
        {
            if (profile != null)
            {
                profile.PropertyChanged -= OnProfilePropertyChanged;
            }
        }

        public void PushIpcConfig()
        {
            if (ActiveProfile == null) return;
            uint mult = ActiveProfile.FrameGenMultiplier switch
            {
                "3X" => 3,
                "4X" => 4,
                _ => 2
            };

            PacingIpcService.Instance.PushConfig(
                0,
                mult,
                ActiveProfile.LatencyTolerance,
                ActiveProfile.SpinWaitThreshold > 0 ? ActiveProfile.SpinWaitThreshold : (float)(ActiveProfile.SpinYieldMicroseconds / 1000.0),
                ActiveProfile.MaxDriftCorrection > 0 ? ActiveProfile.MaxDriftCorrection : 2.0f,
                ActiveProfile.PacingEnabled,
                ActiveProfile.AutoEma,
                ActiveProfile.EmaAlpha
            );

            ActiveProfile.WriteToPublicIni();
        }

        private void OnProfilePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(GameProfile.TargetFpsCap) || e.PropertyName == nameof(GameProfile.TargetFps))
            {
                if (ActiveProfile != null && SelectedTargetFps != ActiveProfile.TargetFpsCap)
                {
                    SelectedTargetFps = ActiveProfile.TargetFpsCap;
                }
            }
            else if (e.PropertyName == nameof(GameProfile.FrameGenMultiplier))
            {
                NotifyMultiplierStates();
                OnPropertyChanged(nameof(CadenceHandshakeReasoning));
                PushIpcConfig();
            }
            else if (e.PropertyName == nameof(GameProfile.EmaAlpha))
            {
                OnPropertyChanged(nameof(SmoothingRecommendationText));
                PushIpcConfig();
            }
            else if (e.PropertyName == nameof(GameProfile.MaxFrameLatency))
            {
                OnPropertyChanged(nameof(QueueDepthRecommendationText));
                PushIpcConfig();
            }
            else if (e.PropertyName == nameof(GameProfile.SpinYieldMicroseconds))
            {
                OnPropertyChanged(nameof(PrecisionTimerRecommendationText));
                PushIpcConfig();
            }
            else
            {
                PushIpcConfig();
            }
        }

        private double _targetFpsSliderIndex = 166; // (180 - 14)
        public double TargetFpsSliderIndex
        {
            get => _targetFpsSliderIndex;
            set
            {
                if (SetProperty(ref _targetFpsSliderIndex, value))
                {
                    int idx = (int)Math.Round(value);
                    // 0 stays 0 (Uncapped). Index 1..486 maps continuously to 15..500 FPS
                    int computedFps = (idx == 0) ? 0 : 14 + idx;
                    
                    if (SelectedTargetFps != computedFps)
                    {
                        SelectedTargetFps = computedFps;
                    }
                }
            }
        }

        private double _sliderPosition = 0;
        public double SliderPosition
        {
            get => _sliderPosition;
            set
            {
                double pos = Math.Round(value);
                if (SetProperty(ref _sliderPosition, pos))
                {
                    _targetFps = (pos == 0) ? 0 : (pos + 14);
                    SelectedTargetFps = (int)_targetFps;

                    try
                    {
                        if (ActiveProfile != null)
                        {
                            ActiveProfile.TargetFps = (int)_targetFps;
                            ActiveProfile.TargetFpsCap = (int)_targetFps;
                        }
                    }
                    catch { }

                    OnPropertyChanged(nameof(TargetFps));
                    OnPropertyChanged(nameof(TargetFramerateCapText));
                    OnPropertyChanged(nameof(TargetIntervalMs));
                    OnPropertyChanged(nameof(EstimatedSleepBudgetMs));
                    NotifyFpsButtons();

                    PushPacingConfig();
                }
            }
        }

        private double _targetFps = 0;
        public double TargetFps
        {
            get => _targetFps;
            set
            {
                double pos = (value <= 0) ? 0 : Math.Max(1, Math.Min(226, value - 14));
                SliderPosition = pos;
            }
        }

        public string TargetFramerateCapText => _targetFps == 0 ? "0 (Uncapped)" : $"{_targetFps} FPS";
        public string TargetFpsDisplay => TargetFramerateCapText;
        public bool EnablePacing => ActiveProfile?.PacingEnabled ?? true;
        public double TargetIntervalMs => _targetFps > 0 ? (1000.0 / _targetFps) : 0.0;
        public double EstimatedSleepBudgetMs => Math.Max(0.0, TargetIntervalMs - 1.2);

        private CancellationTokenSource? _watchdogCts;

        private void TriggerRecoveryWatchdog(uint expectedTargetFps)
        {
            _watchdogCts?.Cancel();
            _watchdogCts = new CancellationTokenSource();
            var token = _watchdogCts.Token;

            Task.Run(async () =>
            {
                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        await Task.Delay(1500, token);
                        if (token.IsCancellationRequested) return;

                        // Re-assert target to IPC if telemetry shows it remains stuck
                        PacingIpcService.Instance.PushConfig(
                            expectedTargetFps,
                            ActiveProfile?.FrameGenMultiplier switch { "3X" => 3, "4X" => 4, _ => 2 },
                            ActiveProfile?.LatencyTolerance ?? 0.5f,
                            ActiveProfile?.SpinWaitThreshold ?? 4.0f,
                            ActiveProfile?.MaxDriftCorrection ?? 2.0f,
                            EnablePacing
                        );
                        AppUI.Services.Telemetry.PresentMonCaptureService.ResetTelemetryBuffer();
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch { }
                }
            }, token);
        }

        public void PushPacingConfig()
        {
            uint fps = (uint)Math.Max(0, _targetFps);
            uint mult = ActiveProfile?.FrameGenMultiplier switch
            {
                "3X" => 3,
                "4X" => 4,
                _ => 2
            };

            PacingIpcService.Instance.PushConfig(
                0,
                mult,
                ActiveProfile?.LatencyTolerance ?? 0.5f,
                ActiveProfile?.SpinWaitThreshold ?? 4.0f,
                ActiveProfile?.MaxDriftCorrection ?? 2.0f,
                EnablePacing,
                ActiveProfile?.AutoEma ?? true,
                ActiveProfile?.EmaAlpha ?? 0.05f
            );

            try { ActiveProfile?.WriteToPublicIni(); } catch { }

            TriggerRecoveryWatchdog(fps);
        }

        [RelayCommand]
        public async Task SetTargetPreset(int fps)
        {
            if (fps <= 0)
            {
                // 1. Instantly tap the 15 FPS queue flush
                SliderPosition = 1; // 1 maps to 15 FPS
                await Task.Delay(16); // 1-frame tick delay
                
                // 2. Snap to Uncapped
                SliderPosition = 0;
            }
            else
            {
                double pos = Math.Max(1, Math.Min(226, fps - 14));
                SliderPosition = pos;
            }
        }

        partial void OnAutoMatchFpsChanged(bool value)
        {
            if (value && ActiveProfile != null)
            {
                UpdateAutoSmoothingWeight();
            }
        }

        private void UpdateAutoSmoothingWeight()
        {
            if (ActiveProfile == null) return;
            int fps = SelectedTargetFps;
            if (fps >= 180) ActiveProfile.EmaAlpha = 0.050f;
            else if (fps >= 144) ActiveProfile.EmaAlpha = 0.065f;
            else if (fps >= 90) ActiveProfile.EmaAlpha = 0.080f;
            else if (fps >= 60) ActiveProfile.EmaAlpha = 0.100f;
            else ActiveProfile.EmaAlpha = 0.050f;

            OnPropertyChanged(nameof(SmoothingRecommendationText));
        }

        public string SelectedProfileName
        {
            get => ActiveProfile?.GameName ?? "Global Default Profile";
            set
            {
                if (ActiveProfile != null && ActiveProfile.GameName != value)
                {
                    OnPropertyChanged(nameof(SelectedProfileName));
                }
            }
        }

        public void NotifyFpsButtons()
        {
            OnPropertyChanged(nameof(IsFps0Active));
            OnPropertyChanged(nameof(IsFps60Active));
            OnPropertyChanged(nameof(IsFps90Active));
            OnPropertyChanged(nameof(IsFps120Active));
            OnPropertyChanged(nameof(IsFps144Active));
            OnPropertyChanged(nameof(IsFps180Active));
        }

        public void NotifyMultiplierStates()
        {
            OnPropertyChanged(nameof(Is1xModeActive));
            OnPropertyChanged(nameof(Is2xModeActive));
            OnPropertyChanged(nameof(Is3xModeActive));
            OnPropertyChanged(nameof(Is4xModeActive));
            OnPropertyChanged(nameof(IsAdaptiveModeActive));
        }

        public void NotifyAllProperties()
        {
            NotifyFpsButtons();
            NotifyMultiplierStates();
            OnPropertyChanged(nameof(SelectedProfileName));
            OnPropertyChanged(nameof(IsExternalLimiterDetected));
            OnPropertyChanged(nameof(IsAutoEmaEnabled));
            OnPropertyChanged(nameof(IsManualEmaAllowed));
            OnPropertyChanged(nameof(EmaSliderOpacity));
            OnPropertyChanged(nameof(EmaAlphaDisplay));
            OnPropertyChanged(nameof(SmoothingRecommendationText));
            OnPropertyChanged(nameof(QueueDepthRecommendationText));
            OnPropertyChanged(nameof(PrecisionTimerRecommendationText));
            OnPropertyChanged(nameof(CadenceHandshakeReasoning));
        }

        [RelayCommand]
        public async Task SetTargetFps(object parameter)
        {
            if (parameter == null) return;
            if (int.TryParse(parameter.ToString(), out int fps))
            {
                await SetTargetPreset(fps);
            }
        }

        [RelayCommand]
        public void SetMultiplier(string multiplier)
        {
            if (ActiveProfile == null) return;
            ActiveProfile.FrameGenMultiplier = multiplier;
            NotifyMultiplierStates();
            OnPropertyChanged(nameof(CadenceHandshakeReasoning));
            PushIpcConfig();
            ActiveProfile.WriteToPublicIni();
        }

        [RelayCommand]
        public void SetFrameGenMultiplier(string multiplier)
        {
            SetMultiplier(multiplier);
        }

        [RelayCommand]
        public void ResetDefaults()
        {
            if (ActiveProfile == null) return;
            ActiveProfile.PacingEnabled = true;
            ActiveProfile.HalfIntervalCadenceEnabled = true;
            ActiveProfile.EmaAlpha = 0.050f;
            ActiveProfile.ForceFlipDiscard = true;
            ActiveProfile.MaxFrameLatency = 1;
            ActiveProfile.SpinYieldMicroseconds = 250;
            ActiveProfile.FrameGenMultiplier = "2X";
            SelectedTargetFps = 180;
            AutoMatchFps = true;

            NotifyAllProperties();
            ActiveProfile.WriteToPublicIni();
        }
    }
}
