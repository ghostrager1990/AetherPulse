using System;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AppUI.Models
{
    public partial class GameProfile : ObservableObject
    {
        [ObservableProperty]
        private string _id = Guid.NewGuid().ToString("N");

        [ObservableProperty]
        private string _gameName = string.Empty;

        [ObservableProperty]
        private string _executablePath = string.Empty;

        [ObservableProperty]
        private string _installDirectory = string.Empty;

        [ObservableProperty]
        private string _iconPath = string.Empty;

        // Pacing / Frame Generation Configuration
        [ObservableProperty]
        private bool _pacingEnabled = true;

        [ObservableProperty]
        private bool _halfIntervalCadenceEnabled = true;

        [ObservableProperty]
        private string _frameGenMultiplier = "Adaptive (Dynamic Target FPS)";

        [ObservableProperty]
        private uint _adaptiveTargetFps = 180; // 60 to 360 FPS

        [ObservableProperty]
        private uint _targetFpsCap = 0; // 0 = automatic EMA tracking

        [ObservableProperty]
        private float _emaAlpha = 0.125f;

        [ObservableProperty]
        private uint _spinYieldMicroseconds = 500;

        [ObservableProperty]
        private bool _forceFlipDiscard = true;

        [ObservableProperty]
        private uint _maxFrameLatency = 1;

        [ObservableProperty]
        private bool _antiLag2Enabled = true;

        [ObservableProperty]
        private bool _hudProtection = true; // HUD Preservation Mask for 2D UI elements

        // Ray Regeneration / Denoiser Configuration
        [ObservableProperty]
        private bool _rayRegenEnabled = true;

        [ObservableProperty]
        private bool _neuralRadianceCache = true; // Neural Radiance Caching (NRC)

        [ObservableProperty]
        private bool _denoiseReflections = true;

        [ObservableProperty]
        private bool _denoiseShadows = true;

        [ObservableProperty]
        private bool _glossyRadianceFilter = true;

        [ObservableProperty]
        private float _roughnessThreshold = 0.5f;

        [ObservableProperty]
        private uint _spatialFilterPasses = 2;

        [ObservableProperty]
        private float _temporalWeight = 0.85f;

        [ObservableProperty]
        private float _depthSigma = 1.0f;

        [ObservableProperty]
        private float _normalSigma = 64.0f;

        [ObservableProperty]
        private bool _forceAutoExposure = true;

        [ObservableProperty]
        private bool _colorSpaceCorrect = true;

        [ObservableProperty]
        private bool _disocclusionFilterEnabled = true;

        [ObservableProperty]
        private bool _forceUnlockInGameRR = true;

        // FidelityFX SDK 2.3 Experimental Flags
        [ObservableProperty]
        private bool _checkerboardRayRecon = false;

        [ObservableProperty]
        private bool _waveletBilateralNormalFilter = true;

        [ObservableProperty]
        private bool _directComputeNrcLatch = true;

        // FSR 4 Upscaling & Sharpening Configuration
        [ObservableProperty]
        private string _upscalingMode = "Quality";

        [ObservableProperty]
        private bool _nativeAA = false; // Native AA (FSR Native resolution render pass)

        [ObservableProperty]
        private bool _reactiveMask = true; // Reactive Mask Optimization for fast HUD elements

        [ObservableProperty]
        private bool _enableRCASOverride = true;

        [ObservableProperty]
        private float _sharpness = 0.35f;

        [ObservableProperty]
        private bool _autoLODBias = true;

        [ObservableProperty]
        private bool _enableDlssSpoofing = true;

        [ObservableProperty]
        private float _textureLODBias = -0.58f;

        [ObservableProperty]
        private float _reactiveMaskSensitivity = 0.10f;

        [ObservableProperty]
        private uint _clampMinRenderScale = 67;

        [ObservableProperty]
        private bool _enableDriverFgLatch = true;

        [ObservableProperty]
        private string _driverFgMultiplier = "2x";

        // Deployment & Runtime State
        [ObservableProperty]
        private DeploymentMode _mode = DeploymentMode.DxcoreProxy;

        public static readonly DeploymentMode[] AvailableModes = new[]
        {
            DeploymentMode.DxcoreProxy,
            DeploymentMode.VersionProxy,
            DeploymentMode.DxgiProxy,
            DeploymentMode.WinMMProxy
        };

        [ObservableProperty]
        private Services.GameCapabilityInfo _capability = new();

                public bool IsAntiLag2ManagedByGame => Capability?.HasNativeAntiLag2 == true;
        public bool CanToggleAntiLag2 => !IsAntiLag2ManagedByGame;
        public string AntiLag2StatusLabel => IsAntiLag2ManagedByGame ? "Managed by Game Engine (Native SDK)" : "DirectX 12 SDK integration aligning game loop cadence with display presentation deadlines.";
        public bool HasCapabilityBadge => !string.IsNullOrEmpty(Capability?.BadgeText);

        public void RefreshCapability()
        {
            if (!string.IsNullOrWhiteSpace(InstallDirectory))
            {
                Capability = Services.GameInspectionService.InspectGame(InstallDirectory);
                OnPropertyChanged(nameof(HasCapabilityBadge));
                OnPropertyChanged(nameof(IsAntiLag2ManagedByGame));
                OnPropertyChanged(nameof(CanToggleAntiLag2));
                OnPropertyChanged(nameof(AntiLag2StatusLabel));
                OnPropertyChanged(nameof(ProxyTypeBadgeText));
                OnPropertyChanged(nameof(ProxyDisplayLabel));
            }
        }

        public string SelectedProxyDll { get; set; } = "version.dll";
        public string ProxyTypeBadgeText => "version.dll (Universal Chainloader)";
        public string ProxyDisplayLabel => "version.dll (Universal Chainloader)";

        [ObservableProperty]
        private string _deploymentStatus = "Inactive";

        partial void OnModeChanged(DeploymentMode value)
        {
            OnPropertyChanged(nameof(ProxyTypeBadgeText));
            OnPropertyChanged(nameof(ProxyDisplayLabel));
            if (!string.IsNullOrWhiteSpace(InstallDirectory))
            {
                var deployService = new Services.DeploymentService();
                IsHookDeployed = deployService.IsDeployed(InstallDirectory, value);
                DeploymentStatus = IsHookDeployed ? "Active" : "Inactive";
            }
        }

        [ObservableProperty]
        private bool _isHookDeployed;

        [ObservableProperty]
        private bool _isGameRunning;

        [ObservableProperty]
        private bool _hasAntiCheatWarning;

        [ObservableProperty]
        private string _detectedAntiCheatName = string.Empty;

        [ObservableProperty]
        private bool _hasOptiScalerConflict;

        [ObservableProperty]
        private string _optiScalerWarningMessage = string.Empty;

        [ObservableProperty]
        private DateTime? _lastDeployedAt;

        public string ExecutableName => !string.IsNullOrEmpty(ExecutablePath) ? Path.GetFileName(ExecutablePath) : string.Empty;

        public string GenerateIniContent()
        {
            return Services.IniConfigService.GenerateCompleteIni(this);
        }

        public void WriteToPublicIni()
        {
            Services.IniConfigService.SaveConfigDebounced(this);
        }
    }
}
