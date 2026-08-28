using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AppUI.Models;

namespace AppUI.ViewModels
{
    public partial class FSRTuningViewModel : ObservableObject
    {
        [ObservableProperty]
        private GameProfile? _activeProfile;

        public bool HasSelectedProfile => ActiveProfile != null;

        public bool IsAutoMipLod
        {
            get => ActiveProfile?.AutoLODBias ?? true;
            set
            {
                if (ActiveProfile != null && ActiveProfile.AutoLODBias != value)
                {
                    ActiveProfile.AutoLODBias = value;
                    if (value)
                    {
                        ActiveProfile.TextureLODBias = -0.58f;
                        OnPropertyChanged(nameof(ManualMipLodBias));
                    }
                    OnPropertyChanged(nameof(IsAutoMipLod));
                    OnPropertyChanged(nameof(LodBiasControlsOpacity));
                }
            }
        }

        public float ManualMipLodBias
        {
            get => ActiveProfile?.TextureLODBias ?? -0.58f;
            set
            {
                if (ActiveProfile != null)
                {
                    ActiveProfile.TextureLODBias = value;
                    if (ActiveProfile.AutoLODBias)
                    {
                        ActiveProfile.AutoLODBias = false;
                        OnPropertyChanged(nameof(IsAutoMipLod));
                        OnPropertyChanged(nameof(LodBiasControlsOpacity));
                    }
                    OnPropertyChanged(nameof(ManualMipLodBias));
                }
            }
        }

        public double LodBiasControlsOpacity => (ActiveProfile?.AutoLODBias ?? true) ? 0.65 : 1.0;

        public bool IsDrsFloorEnabled => !(ActiveProfile?.NativeAA ?? false);
        public string DrsFloorDisplayText => (ActiveProfile?.NativeAA ?? false)
            ? "100% (Native AA Locked)"
            : $"{ActiveProfile?.ClampMinRenderScale ?? 67}%";
        public double DrsFloorOpacity => (ActiveProfile?.NativeAA ?? false) ? 0.65 : 1.0;

        public string RecommendedSharpnessText => "Recommended: 0.35 (Optimal edge clarity with zero ringing artifacts)";
        public string RecommendedLodBiasText => "Recommended: Auto (-0.58 for Quality, -1.00 for Balanced)";
        public string RecommendedReactiveMaskText => "Recommended: 0.10 (Eliminates particle and spell trail ghosting)";
        public string RecommendedDrsFloorText => (ActiveProfile?.NativeAA ?? false)
            ? "Disabled: Native AA mode enforces a strict 1.0x render scale."
            : "Recommended: 67% (Preserves high-frequency details during heavy action)";

        public void SetProfile(GameProfile? profile)
        {
            ActiveProfile = profile;
        }

        partial void OnActiveProfileChanged(GameProfile? oldValue, GameProfile? newValue)
        {
            if (oldValue != null)
            {
                oldValue.PropertyChanged -= OnProfilePropertyChanged;
            }

            if (newValue != null)
            {
                newValue.PropertyChanged += OnProfilePropertyChanged;
            }

            OnPropertyChanged(nameof(HasSelectedProfile));
            OnPropertyChanged(nameof(IsAutoMipLod));
            OnPropertyChanged(nameof(ManualMipLodBias));
            OnPropertyChanged(nameof(LodBiasControlsOpacity));
            OnPropertyChanged(nameof(IsDrsFloorEnabled));
            OnPropertyChanged(nameof(DrsFloorDisplayText));
            OnPropertyChanged(nameof(DrsFloorOpacity));
            OnPropertyChanged(nameof(RecommendedDrsFloorText));
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

        private void OnProfilePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            SyncConfigToDisk();

            if (e.PropertyName == nameof(GameProfile.AutoLODBias))
            {
                OnPropertyChanged(nameof(IsAutoMipLod));
                OnPropertyChanged(nameof(LodBiasControlsOpacity));
            }
            else if (e.PropertyName == nameof(GameProfile.TextureLODBias))
            {
                OnPropertyChanged(nameof(ManualMipLodBias));
            }
            else if (e.PropertyName == nameof(GameProfile.NativeAA))
            {
                OnPropertyChanged(nameof(IsDrsFloorEnabled));
                OnPropertyChanged(nameof(DrsFloorDisplayText));
                OnPropertyChanged(nameof(DrsFloorOpacity));
                OnPropertyChanged(nameof(RecommendedDrsFloorText));
            }
            else if (e.PropertyName == nameof(GameProfile.ClampMinRenderScale))
            {
                OnPropertyChanged(nameof(DrsFloorDisplayText));
            }
        }

        [RelayCommand]
        public void ApplySoftPreset()
        {
            if (ActiveProfile != null)
            {
                ActiveProfile.EnableRCASOverride = true;
                ActiveProfile.Sharpness = 0.15f;
            }
        }

        [RelayCommand]
        public void ApplyBalancedPreset()
        {
            if (ActiveProfile != null)
            {
                ActiveProfile.EnableRCASOverride = true;
                ActiveProfile.Sharpness = 0.35f;
            }
        }

        [RelayCommand]
        public void ApplyUltraSharpPreset()
        {
            if (ActiveProfile != null)
            {
                ActiveProfile.EnableRCASOverride = true;
                ActiveProfile.Sharpness = 0.60f;
            }
        }

        [RelayCommand]
        public void SetDrsQuality()
        {
            if (ActiveProfile != null && !(ActiveProfile.NativeAA))
            {
                ActiveProfile.ClampMinRenderScale = 67;
            }
        }

        [RelayCommand]
        public void SetDrsBalanced()
        {
            if (ActiveProfile != null && !(ActiveProfile.NativeAA))
            {
                ActiveProfile.ClampMinRenderScale = 59;
            }
        }

        [RelayCommand]
        public void SetDrsPerformance()
        {
            if (ActiveProfile != null && !(ActiveProfile.NativeAA))
            {
                ActiveProfile.ClampMinRenderScale = 50;
            }
        }

        public string[] MultiplierOptions { get; } = new[] { "2x", "3x", "4x" };

        [RelayCommand]
        public void SetMultiplier(string multiplier)
        {
            if (ActiveProfile != null)
            {
                ActiveProfile.DriverFgMultiplier = multiplier;
                OnPropertyChanged(nameof(ActiveProfile));
            }
        }
    }
}
