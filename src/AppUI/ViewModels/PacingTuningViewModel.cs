using System.Collections.Generic;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AppUI.Models;

namespace AppUI.ViewModels
{
    public partial class PacingTuningViewModel : ObservableObject
    {
        [ObservableProperty]
        private GameProfile? _activeProfile;

        public bool HasSelectedProfile => ActiveProfile != null;

        public float RecommendedAlpha
        {
            get
            {
                uint fps = ActiveProfile?.TargetFpsCap ?? 60;
                if (fps == 0) fps = 60; // Default when unbounded

                if (fps <= 45) return 0.250f;
                if (fps <= 75) return 0.125f;
                if (fps <= 110) return 0.080f;
                if (fps <= 150) return 0.050f;
                return 0.030f;
            }
        }

        public string RecommendedAlphaText
        {
            get
            {
                uint fps = ActiveProfile?.TargetFpsCap ?? 60;
                if (fps == 0) fps = 60; // Default when unbounded

                if (fps <= 45) return "0.250 (7-frame window)";
                if (fps <= 75) return "0.125 (16-frame window)";
                if (fps <= 110) return "0.080 (24-frame window)";
                if (fps <= 150) return "0.050 (40-frame window)";
                return "0.030 (65-frame window)";
            }
        }

        public void SetProfile(GameProfile? profile)
        {
            if (ActiveProfile != null)
            {
                ActiveProfile.PropertyChanged -= OnProfilePropertyChanged;
            }

            ActiveProfile = profile;

            if (ActiveProfile != null)
            {
                ActiveProfile.PropertyChanged += OnProfilePropertyChanged;
            }

            OnPropertyChanged(nameof(HasSelectedProfile));
            RefreshRecommendations();
        }

        private void OnProfilePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            SyncConfigToDisk();

            if (e.PropertyName == nameof(GameProfile.TargetFpsCap) ||
                e.PropertyName == nameof(GameProfile.EmaAlpha))
            {
                RefreshRecommendations();
            }
        }

        private void SyncConfigToDisk()
        {
            if (ActiveProfile == null) return;
            ActiveProfile.WriteToPublicIni();
            if (string.IsNullOrEmpty(ActiveProfile.InstallDirectory)) return;
            try
            {
                string iniPath = System.IO.Path.Combine(ActiveProfile.InstallDirectory, "aetherpulse.ini");
                System.IO.File.WriteAllText(iniPath, ActiveProfile.GenerateIniContent());
            }
            catch
            {
            }
        }

        private void RefreshRecommendations()
        {
            OnPropertyChanged(nameof(RecommendedAlpha));
            OnPropertyChanged(nameof(RecommendedAlphaText));
            OnPropertyChanged(nameof(TargetFpsInputText));
            OnPropertyChanged(nameof(EmaAlphaInputText));
            OnPropertyChanged(nameof(MaxFrameLatencyInputText));
            OnPropertyChanged(nameof(SpinYieldInputText));
        }

        [RelayCommand]
        public void MatchTargetFps()
        {
            if (ActiveProfile != null)
            {
                ActiveProfile.EmaAlpha = RecommendedAlpha;
            }
        }

        [RelayCommand]
        public void ResetDefaults()
        {
            if (ActiveProfile == null) return;

            ActiveProfile.PacingEnabled = true;
            ActiveProfile.HalfIntervalCadenceEnabled = true;
            ActiveProfile.AntiLag2Enabled = true;
            ActiveProfile.HudProtection = true;
            ActiveProfile.TargetFpsCap = 0;
            ActiveProfile.EmaAlpha = 0.125f;
            ActiveProfile.ForceFlipDiscard = true;
            ActiveProfile.MaxFrameLatency = 1;
            ActiveProfile.SpinYieldMicroseconds = 500;

            RefreshRecommendations();
        }

        public uint[] TargetFpsPresets { get; } = new uint[] { 0, 30, 45, 60, 75, 90, 120, 144, 180 };

        [RelayCommand]
        public void SetTargetFpsPreset(object? param)
        {
            if (ActiveProfile == null || param == null) return;
            if (uint.TryParse(param.ToString(), out uint fps))
            {
                ActiveProfile.TargetFpsCap = fps;
                RefreshRecommendations();
            }
        }

        // Editable numeric properties (pure numbers)
        public string TargetFpsInputText
        {
            get => $"{ActiveProfile?.TargetFpsCap ?? 0}";
            set
            {
                if (ActiveProfile == null) return;
                string clean = value.Replace("FPS", "", System.StringComparison.OrdinalIgnoreCase).Trim();
                if (uint.TryParse(clean, out uint val))
                {
                    ActiveProfile.TargetFpsCap = System.Math.Clamp(val, 0u, 240u);
                }
                OnPropertyChanged(nameof(TargetFpsInputText));
            }
        }

        public string EmaAlphaInputText
        {
            get => $"{ActiveProfile?.EmaAlpha ?? 0.125f:F3}";
            set
            {
                if (ActiveProfile == null) return;
                if (float.TryParse(value.Trim(), out float val))
                {
                    ActiveProfile.EmaAlpha = System.Math.Clamp(val, 0.001f, 1.000f);
                }
                OnPropertyChanged(nameof(EmaAlphaInputText));
            }
        }

        public string MaxFrameLatencyInputText
        {
            get => $"{ActiveProfile?.MaxFrameLatency ?? 1}";
            set
            {
                if (ActiveProfile == null) return;
                string clean = value.Replace("Frame", "", System.StringComparison.OrdinalIgnoreCase).Replace("s", "", System.StringComparison.OrdinalIgnoreCase).Trim();
                if (uint.TryParse(clean, out uint val))
                {
                    ActiveProfile.MaxFrameLatency = System.Math.Clamp(val, 1u, 4u);
                }
                OnPropertyChanged(nameof(MaxFrameLatencyInputText));
            }
        }

        public string SpinYieldInputText
        {
            get => $"{ActiveProfile?.SpinYieldMicroseconds ?? 500}";
            set
            {
                if (ActiveProfile == null) return;
                string clean = value.Replace("µs", "", System.StringComparison.OrdinalIgnoreCase).Replace("us", "", System.StringComparison.OrdinalIgnoreCase).Replace("μs", "", System.StringComparison.OrdinalIgnoreCase).Trim();
                if (uint.TryParse(clean, out uint val))
                {
                    ActiveProfile.SpinYieldMicroseconds = System.Math.Clamp(val, 0u, 2000u);
                }
                OnPropertyChanged(nameof(SpinYieldInputText));
            }
        }
    }
}
