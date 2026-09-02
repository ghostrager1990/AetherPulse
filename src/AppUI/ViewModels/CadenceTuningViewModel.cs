using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AppUI.Models;

namespace AppUI.ViewModels
{
    public partial class CadenceTuningViewModel : ObservableObject
    {
        [ObservableProperty]
        private GameProfile _activeProfile;

        [ObservableProperty]
        private ObservableCollection<GameProfile> _availableProfiles = new();

        [ObservableProperty]
        private int _selectedTargetFps = 180;

        [ObservableProperty]
        private bool _autoMatchFps = true;

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
        public bool IsFps180Active => SelectedTargetFps == 180;

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
                    ActiveProfile.WriteToPublicIni();
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

        public CadenceTuningViewModel()
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

        public CadenceTuningViewModel(object? p1) : this() { }
        public CadenceTuningViewModel(object? p1, object? p2) : this() { }
        public CadenceTuningViewModel(object? p1, object? p2, object? p3) : this() { }

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
                ActiveProfile?.WriteToPublicIni();
            }
            else if (e.PropertyName == nameof(GameProfile.EmaAlpha))
            {
                OnPropertyChanged(nameof(SmoothingRecommendationText));
                ActiveProfile?.WriteToPublicIni();
            }
            else if (e.PropertyName == nameof(GameProfile.MaxFrameLatency))
            {
                OnPropertyChanged(nameof(QueueDepthRecommendationText));
                ActiveProfile?.WriteToPublicIni();
            }
            else if (e.PropertyName == nameof(GameProfile.SpinYieldMicroseconds))
            {
                OnPropertyChanged(nameof(PrecisionTimerRecommendationText));
                ActiveProfile?.WriteToPublicIni();
            }
            else
            {
                ActiveProfile?.WriteToPublicIni();
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

        public string TargetFpsDisplay => SelectedTargetFps == 0 ? "0 (Uncapped)" : $"{SelectedTargetFps} FPS";
        public string TargetFramerateCapText => TargetFpsDisplay;

        public int TargetFps
        {
            get => SelectedTargetFps;
            set => SelectedTargetFps = value;
        }

        public double SliderPosition
        {
            get => SelectedTargetFps == 0 ? 0 : (SelectedTargetFps - 14);
            set
            {
                double pos = Math.Round(value);
                int mappedFps = pos == 0 ? 0 : (int)(pos + 14);
                SelectedTargetFps = mappedFps;
            }
        }

        partial void OnSelectedTargetFpsChanged(int value)
        {
            OnPropertyChanged(nameof(TargetFps));
            OnPropertyChanged(nameof(TargetFramerateCapText));
            OnPropertyChanged(nameof(SliderPosition));
            // Keep Slider Index synchronized when changed via presets, profile load, or Auto Match
            double mappedIdx = (value <= 0) ? 0 : Math.Clamp(value - 14, 1, 486);
            if (Math.Abs(_targetFpsSliderIndex - mappedIdx) > 0.001)
            {
                _targetFpsSliderIndex = mappedIdx;
                OnPropertyChanged(nameof(TargetFpsSliderIndex));
            }

            if (ActiveProfile != null)
            {
                ActiveProfile.TargetFpsCap = value;
                ActiveProfile.TargetFps = value;
            }

            OnPropertyChanged(nameof(TargetFpsDisplay));
            NotifyFpsButtons();
            if (AutoMatchFps)
            {
                UpdateAutoSmoothingWeight();
            }
            OnPropertyChanged(nameof(CadenceHandshakeReasoning));
            ActiveProfile?.WriteToPublicIni();
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
        public void SetTargetFps(object parameter)
        {
            if (parameter == null) return;
            if (int.TryParse(parameter.ToString(), out int fps))
            {
                SelectedTargetFps = fps;
            }
        }

        [RelayCommand]
        public void SetMultiplier(string multiplier)
        {
            if (ActiveProfile == null) return;
            ActiveProfile.FrameGenMultiplier = multiplier;
            NotifyMultiplierStates();
            OnPropertyChanged(nameof(CadenceHandshakeReasoning));
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
