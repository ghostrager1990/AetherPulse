using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AppUI.Models;

namespace AppUI.ViewModels
{
    public partial class RayRegenTuningViewModel : ObservableObject
    {
        [ObservableProperty]
        private GameProfile? _activeProfile;

        [ObservableProperty]
        private bool _isPresetModalVisible;

        [ObservableProperty]
        private string _modalTitle = string.Empty;

        [ObservableProperty]
        private string _modalDescription = string.Empty;

        [ObservableProperty]
        private string _modalGpu = string.Empty;

        [ObservableProperty]
        private string _modalVram = string.Empty;

        [ObservableProperty]
        private string _selectedPresetKey = string.Empty;

        public bool HasSelectedProfile => ActiveProfile != null;

        public void SetProfile(GameProfile? profile)
        {
            ActiveProfile = profile;
            OnPropertyChanged(nameof(HasSelectedProfile));
        }

        [RelayCommand]
        public void OpenPresetModal(string presetKey)
        {
            SelectedPresetKey = presetKey;
            switch (presetKey)
            {
                case "Ultra":
                    ModalTitle = "Apply Ultra Ray Tracing Preset";
                    ModalDescription = "Enables full Neural Radiance Caching (NRC), reflection & shadow denoising, glossy filtering, and color correction with an extensive 3-pass spatial-temporal filter (0.90 history, 0.30 roughness, 1.50 depth, 96.0 normal). Optimized for high-resolution path tracing with maximum specular clarity.";
                    ModalGpu = "Radeon RX 9000 Series (RDNA 4) / RX 7900 XT / XTX";
                    ModalVram = "16 GB";
                    break;

                case "Performance":
                    ModalTitle = "Apply High Performance Preset";
                    ModalDescription = "Configures a lightweight 1-pass analytical wavelet filter (0.75 history, 0.65 roughness, 0.80 depth, 32.0 normal). Heavy modules (NRC, Shadow Denoiser, Glossy Radiance Filter, and Non-Linear Color Correction) are automatically bypassed to maximize frame rates on mainstream and mobile GPUs.";
                    ModalGpu = "Radeon RX 6600 / RX 6000 Series & APUs";
                    ModalVram = "8 GB";
                    break;

                case "Reset":
                    ModalTitle = "Reset Ray Regeneration to Defaults";
                    ModalDescription = "Restores all ray reconstruction switches (NRC, Reflections, Shadows, Glossy, Color Correction) to Enabled, sets Roughness Threshold to 0.50, Wavelet Filter Passes to 2, Temporal History Weight to 0.85, Depth Sigma to 1.00, and Normal Sigma to 64.0.";
                    ModalGpu = "Any DirectX 12 / Vulkan Compatible GPU";
                    ModalVram = "Any Supported VRAM";
                    break;

                case "Balanced":
                default:
                    SelectedPresetKey = "Balanced";
                    ModalTitle = "Apply Balanced Preset";
                    ModalDescription = "Enables Neural Radiance Caching (NRC), reflection & shadow denoising, glossy filtering, and color correction with a 2-pass spatial-temporal wavelet filter (0.85 history, 0.50 roughness, 1.00 depth, 64.0 normal). Delivers pristine reflections with zero ghosting.";
                    ModalGpu = "Radeon RX 7000 Series / RX 6700 XT+";
                    ModalVram = "12 GB";
                    break;
            }

            IsPresetModalVisible = true;
        }

        [RelayCommand]
        public void ClosePresetModal()
        {
            IsPresetModalVisible = false;
        }

        [RelayCommand]
        public void ApplySelectedPreset()
        {
            if (ActiveProfile != null)
            {
                switch (SelectedPresetKey)
                {
                    case "Ultra":
                        ActiveProfile.NeuralRadianceCache = true;
                        ActiveProfile.DenoiseReflections = true;
                        ActiveProfile.DenoiseShadows = true;
                        ActiveProfile.GlossyRadianceFilter = true;
                        ActiveProfile.ColorSpaceCorrect = true;
                        ActiveProfile.SpatialFilterPasses = 3;
                        ActiveProfile.TemporalWeight = 0.90f;
                        ActiveProfile.RoughnessThreshold = 0.30f;
                        ActiveProfile.DepthSigma = 1.50f;
                        ActiveProfile.NormalSigma = 96.0f;
                        ActiveProfile.ForceAutoExposure = true;
                        ActiveProfile.DisocclusionFilterEnabled = true;
                        break;

                    case "Performance":
                        ActiveProfile.NeuralRadianceCache = false;
                        ActiveProfile.DenoiseReflections = true;
                        ActiveProfile.DenoiseShadows = false;
                        ActiveProfile.GlossyRadianceFilter = false;
                        ActiveProfile.ColorSpaceCorrect = false;
                        ActiveProfile.SpatialFilterPasses = 1;
                        ActiveProfile.TemporalWeight = 0.75f;
                        ActiveProfile.RoughnessThreshold = 0.65f;
                        ActiveProfile.DepthSigma = 0.80f;
                        ActiveProfile.NormalSigma = 32.0f;
                        ActiveProfile.ForceAutoExposure = true;
                        ActiveProfile.DisocclusionFilterEnabled = true;
                        break;

                    case "Reset":
                    case "Balanced":
                    default:
                        ActiveProfile.NeuralRadianceCache = true;
                        ActiveProfile.DenoiseReflections = true;
                        ActiveProfile.DenoiseShadows = true;
                        ActiveProfile.GlossyRadianceFilter = true;
                        ActiveProfile.ColorSpaceCorrect = true;
                        ActiveProfile.SpatialFilterPasses = 2;
                        ActiveProfile.TemporalWeight = 0.85f;
                        ActiveProfile.RoughnessThreshold = 0.50f;
                        ActiveProfile.DepthSigma = 1.00f;
                        ActiveProfile.NormalSigma = 64.0f;
                        ActiveProfile.ForceAutoExposure = true;
                        ActiveProfile.DisocclusionFilterEnabled = true;
                        break;
                }
            }

            IsPresetModalVisible = false;
        }

        [RelayCommand]
        public void ApplyHighPerformancePreset()
        {
            OpenPresetModal("Performance");
        }

        [RelayCommand]
        public void ApplyBalancedPreset()
        {
            OpenPresetModal("Balanced");
        }

        [RelayCommand]
        public void ApplyUltraPreset()
        {
            OpenPresetModal("Ultra");
        }

        [RelayCommand]
        public void ApplyPerformancePreset()
        {
            OpenPresetModal("Performance");
        }

        [RelayCommand]
        public void ApplyPreset(string preset)
        {
            OpenPresetModal(preset);
        }
    }
}
