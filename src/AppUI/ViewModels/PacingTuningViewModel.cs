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

        public List<string> MultiplierOptions { get; } = new()
        {
            "Adaptive (Dynamic Target FPS)",
            "Off (1x Native Passthrough)",
            "2x (Standard Frame Gen)",
            "3x (High Refresh)",
            "4x (Ultra Cadence)",
            "5x",
            "6x (Maximum Interpolation)"
        };

        public bool IsAdaptiveTargetFpsEnabled => ActiveProfile?.FrameGenMultiplier?.Contains("Adaptive") == true;
        public double AdaptiveFpsOpacity => IsAdaptiveTargetFpsEnabled ? 1.0 : 0.65;

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
            OnPropertyChanged(nameof(IsAdaptiveTargetFpsEnabled));
            OnPropertyChanged(nameof(AdaptiveFpsOpacity));
            RefreshRecommendations();
        }

        private void OnProfilePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(GameProfile.TargetFpsCap) ||
                e.PropertyName == nameof(GameProfile.EmaAlpha))
            {
                RefreshRecommendations();
            }
            else if (e.PropertyName == nameof(GameProfile.FrameGenMultiplier))
            {
                OnPropertyChanged(nameof(IsAdaptiveTargetFpsEnabled));
                OnPropertyChanged(nameof(AdaptiveFpsOpacity));
            }
        }

        private void RefreshRecommendations()
        {
            OnPropertyChanged(nameof(RecommendedAlpha));
            OnPropertyChanged(nameof(RecommendedAlphaText));
        }

        [RelayCommand]
        public void MatchTargetFps()
        {
            if (ActiveProfile != null)
            {
                ActiveProfile.EmaAlpha = RecommendedAlpha;
            }
        }
    }
}
