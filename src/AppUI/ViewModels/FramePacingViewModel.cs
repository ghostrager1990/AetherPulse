using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AppUI.Models;
using AppUI.Services.Pacing;

namespace AppUI.ViewModels
{
    public partial class FramePacingViewModel : ObservableObject
    {
        [ObservableProperty]
        private GameProfile? _activeProfile;

        [ObservableProperty]
        private ObservableCollection<GameProfile> _availableProfiles = new();

        private double _sliderPosition = 0;
        public double SliderPosition
        {
            get => _sliderPosition;
            set
            {
                double pos = Math.Round(value);
                if (SetProperty(ref _sliderPosition, pos))
                {
                    // Map 0 -> 0 (Uncapped), 1..226 -> 15..240 FPS
                    _targetFps = (pos == 0) ? 0 : (pos + 14);

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

                    // Slider is the exclusive trigger for the IPC handshake
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
                // When external updates arrive, set the physical slider position
                double pos = (value <= 0) ? 0 : Math.Max(1, Math.Min(226, value - 14));
                SliderPosition = pos;
            }
        }

        [ObservableProperty]
        private bool _enablePacing = true;

        [ObservableProperty]
        private bool _isDebugViewExpanded;

        public bool Is1xModeActive => ActiveProfile?.FrameGenMultiplier == "1X";
        public bool Is2xModeActive => ActiveProfile?.FrameGenMultiplier == "2X" || string.IsNullOrEmpty(ActiveProfile?.FrameGenMultiplier);
        public bool Is3xModeActive => ActiveProfile?.FrameGenMultiplier == "3X";
        public bool Is4xModeActive => ActiveProfile?.FrameGenMultiplier == "4X";

        public string TargetFramerateCapText => TargetFps == 0 ? "0 (Uncapped)" : $"{TargetFps} FPS";
        public double TargetIntervalMs => TargetFps > 0 ? (1000.0 / TargetFps) : 0.0;
        public double EstimatedSleepBudgetMs => Math.Max(0.0, TargetIntervalMs - 1.2);

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

        public string PacerHookStatus
        {
            get
            {
                if (IsExternalLimiterDetected) return "AUTO-PASSTHROUGH (RTSS Limiter Detected)";
                if (!EnablePacing) return "BYPASS (Engine Native)";
                bool isHooked = PacingIpcService.Instance.PollHookHandshake();
                return isHooked ? "ACTIVE (Hardware VMT Scanout)" : "ENGAGED (Waiting for Game Flip)";
            }
        }

        public FramePacingViewModel()
        {
            var defaultProfile = new GameProfile
            {
                Name = "Default",
                EnableAdvancedPacing = true,
                FrameGenMultiplier = "2X",
                TargetFps = 0,
                LatencyTolerance = 0.5f,
                SpinWaitThreshold = 4.0f,
                MaxDriftCorrection = 2.0f,
                EnableFastPathPresent = true,
                EnableReflexSpoof = true
            };
            _activeProfile = defaultProfile;
            _targetFps = defaultProfile.TargetFps;
            _enablePacing = defaultProfile.EnableAdvancedPacing;

            AppUI.Services.Telemetry.TelemetryHub.Instance.OnTelemetryUpdated += (snap) =>
            {
                OnPropertyChanged(nameof(IsExternalLimiterDetected));
                OnPropertyChanged(nameof(PacerHookStatus));
            };
        }

        public FramePacingViewModel(object? p1) : this() { }
        public FramePacingViewModel(object? p1, object? p2) : this() { }
        public FramePacingViewModel(object? p1, object? p2, object? p3) : this() { }

        partial void OnEnablePacingChanged(bool value)
        {
            if (ActiveProfile != null) ActiveProfile.EnableAdvancedPacing = value;
            OnPropertyChanged(nameof(PacerHookStatus));
            PushPacingConfig();
        }

        public void PushPacingConfig()
        {
            uint fps = (uint)Math.Max(0, TargetFps);
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

            try
            {
                ActiveProfile?.WriteToPublicIni();
            }
            catch { }
        }

        [RelayCommand]
        public void SetTargetPreset(int fps)
        {
            // Preset buttons ONLY move the physical slider position. No direct IPC here.
            if (fps <= 0)
            {
                SliderPosition = 0;
            }
            else
            {
                double pos = Math.Max(1, Math.Min(226, fps - 14));
                SliderPosition = pos;
            }
        }

        [RelayCommand]
        public void SetMultiplier(string multiplier)
        {
            if (ActiveProfile == null) return;
            ActiveProfile.FrameGenMultiplier = multiplier;
            OnPropertyChanged(nameof(Is1xModeActive));
            OnPropertyChanged(nameof(Is2xModeActive));
            OnPropertyChanged(nameof(Is3xModeActive));
            OnPropertyChanged(nameof(Is4xModeActive));
            PushPacingConfig();
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

        public void SetProfile(GameProfile? profile)
        {
            if (profile != null)
            {
                ActiveProfile = profile;
                TargetFps = profile.TargetFpsCap;
                EnablePacing = profile.EnableAdvancedPacing;
                OnPropertyChanged(nameof(SelectedProfileName));
                OnPropertyChanged(nameof(PacerHookStatus));
                PushPacingConfig();
            }
        }

        [RelayCommand]
        public void ResetDefaults()
        {
            SetMultiplier("2X");
            TargetFps = 0;
            EnablePacing = true;
        }
    }
}
