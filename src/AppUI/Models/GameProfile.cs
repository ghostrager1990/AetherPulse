using System;
using System.IO;
using System.Text;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AppUI.Models
{
    public class GameCapability
    {
        public string BadgeText { get; set; } = "DirectX Presentation Hook / Cadence Pacer";
    }

    public partial class GameProfile : ObservableObject, IEquatable<GameProfile>
    {
        [ObservableProperty]
        private string _id = Guid.NewGuid().ToString();

        [ObservableProperty]
        private string _name = string.Empty;

        [JsonIgnore]
        public string GameName
        {
            get => Name;
            set => Name = value;
        }

        [ObservableProperty]
        private string _executablePath = string.Empty;

        [JsonIgnore]
        public string ExecutableName => Path.GetFileName(ExecutablePath);

        private string _installDirectory = string.Empty;
        public string InstallDirectory
        {
            get => !string.IsNullOrEmpty(_installDirectory) 
                ? _installDirectory 
                : (!string.IsNullOrEmpty(ExecutablePath) ? (File.Exists(ExecutablePath) ? (Path.GetDirectoryName(ExecutablePath) ?? "") : ExecutablePath) : "");
            set => SetProperty(ref _installDirectory, value);
        }

        [ObservableProperty]
        private string _launchArguments = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsHookDeployed))]
        private bool _isDeployed;

        [JsonIgnore]
        public bool IsHookDeployed => IsDeployed;

        [ObservableProperty]
        [property: JsonIgnore]
        private bool _isGameRunning = false;

        [ObservableProperty]
        private bool _hasAntiCheatWarning = false;

        [ObservableProperty]
        private bool _hasCapabilityBadge = true;

        [ObservableProperty]
        private GameCapability _capability = new GameCapability();

        [ObservableProperty]
        private bool _hasOptiScalerConflict = false;

        [ObservableProperty]
        private string _optiScalerWarningMessage = "OptiScaler mod artifacts detected in game folder. Click to clean without affecting ReShade.";

        [ObservableProperty]
        private DeploymentMode _mode = DeploymentMode.DxgiProxy;

        // --- Multi-Frame & Cadence Properties ---
        [ObservableProperty]
        private string _frameGenMultiplier = "2X";

        [ObservableProperty]
        private int _targetFpsCap = 180;

        [JsonIgnore]
        public int TargetFps
        {
            get => TargetFpsCap;
            set => TargetFpsCap = value;
        }

        [ObservableProperty]
        private bool _enableAdvancedPacing = true;

        [ObservableProperty]
        private bool _pacingEnabled = true;

        [JsonIgnore]
        public bool EnablePacing
        {
            get => PacingEnabled;
            set => PacingEnabled = value;
        }

        [ObservableProperty]
        private float _latencyTolerance = 0.5f;

        [ObservableProperty]
        private float _spinWaitThreshold = 4.0f;

        [ObservableProperty]
        private float _maxDriftCorrection = 2.0f;

        [ObservableProperty]
        private bool _enableFastPathPresent = true;

        [ObservableProperty]
        private bool _enableReflexSpoof = true;

        [ObservableProperty]
        private bool _enableCameraCutRearm = true;

        [ObservableProperty]
        private bool _isolateHudLayers = true;

        [ObservableProperty]
        private bool _halfIntervalCadenceEnabled = true;

        [ObservableProperty]
        private bool _antiLag2Enabled = true;

        [ObservableProperty]
        private bool _hudProtection = true;

        [ObservableProperty]
        private bool _autoEma = true;

        [ObservableProperty]
        private float _emaAlpha = 0.050f;

        [ObservableProperty]
        private bool _forceFlipDiscard = true;

        [ObservableProperty]
        private int _maxFrameLatency = 1;

        [ObservableProperty]
        private uint _spinYieldMicroseconds = 250;

        // --- FSR / RCAS Tuning Properties (Pillar 2) ---
        [ObservableProperty]
        private bool _nativeAA = false;

        [ObservableProperty]
        private bool _autoLODBias = true;

        [JsonIgnore]
        public bool AutoMipLodBias
        {
            get => AutoLODBias;
            set => AutoLODBias = value;
        }

        [ObservableProperty]
        private float _textureLODBias = -0.5f;

        [JsonIgnore]
        public float ManualMipLodBias
        {
            get => TextureLODBias;
            set => TextureLODBias = value;
        }

        [ObservableProperty]
        private bool _enableRCASOverride = true;

        [JsonIgnore]
        public bool RcasEnabled
        {
            get => EnableRCASOverride;
            set => EnableRCASOverride = value;
        }

        [JsonIgnore]
        public bool EnableRcas
        {
            get => EnableRCASOverride;
            set => EnableRCASOverride = value;
        }

        [ObservableProperty]
        private float _sharpness = 0.35f;

        [JsonIgnore]
        public float RcasSharpness
        {
            get => Sharpness;
            set => Sharpness = value;
        }

        [ObservableProperty]
        private bool _reactiveMask = true;

        [ObservableProperty]
        private float _reactiveMaskSensitivity = 0.85f;

        [ObservableProperty]
        private bool _clampMinRenderScale = false;

        [ObservableProperty]
        private int _minRenderScalePercent = 67;

        [ObservableProperty]
        private string _driverFgMultiplier = "2x";

        // --- Ray Regeneration / FidelityFX Properties ---
        [ObservableProperty]
        private bool _rayRegenEnabled = true;

        [JsonIgnore]
        public bool EnableRayRegen
        {
            get => RayRegenEnabled;
            set => RayRegenEnabled = value;
        }

        [ObservableProperty]
        private bool _checkerboardRayRecon = false;

        [ObservableProperty]
        private bool _waveletBilateralNormalFilter = true;

        [ObservableProperty]
        private bool _directComputeNrcLatch = false;

        [ObservableProperty]
        private bool _neuralRadianceCache = true;

        [ObservableProperty]
        private bool _denoiseReflections = true;

        [ObservableProperty]
        private bool _denoiseShadows = true;

        [ObservableProperty]
        private bool _glossyRadianceFilter = true;

        [ObservableProperty]
        private bool _colorSpaceCorrect = true;

        [ObservableProperty]
        private int _spatialFilterPasses = 2;

        [ObservableProperty]
        private float _temporalWeight = 0.85f;

        [ObservableProperty]
        private float _roughnessThreshold = 0.50f;

        [ObservableProperty]
        private float _depthSigma = 1.0f;

        [ObservableProperty]
        private float _normalSigma = 64.0f;

        [ObservableProperty]
        private bool _forceAutoExposure = false;

        [ObservableProperty]
        private bool _disocclusionFilterEnabled = true;

        // --- Helpers & Badges ---
        [JsonIgnore]
        public System.Windows.Media.ImageSource? GameIcon => !string.IsNullOrEmpty(ExecutablePath) ? AppUI.Helpers.IconHelper.ExtractIconFromExe(ExecutablePath) : null;
        
        [JsonIgnore]
        public bool HasGameIcon => GameIcon != null;

        [JsonIgnore]
        public string SelectedProxyDll => "dxgi.dll";
        
        [JsonIgnore]
        public string ProxyTypeBadgeText => "DXGI Cadence Pacer";
        
        [JsonIgnore]
        public string ProxyDisplayLabel => "dxgi.dll (Presentation Hook)";

        public void CheckAntiCheatProtection()
        {
            if (string.IsNullOrWhiteSpace(ExecutablePath) || !File.Exists(ExecutablePath))
            {
                HasAntiCheatWarning = false;
                return;
            }

            try
            {
                var dir = Path.GetDirectoryName(ExecutablePath);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                {
                    HasAntiCheatWarning = false;
                    return;
                }

                string[] acSignatures = new[] { 
                    "easyanticheat", "eac_server", "start_protected_game", 
                    "beservice", "bedaisy", "battleye", 
                    "vgk", "ace-base", "anticheatexpert", "equ8", "xigncode" 
                };

                bool detected = false;
                var files = Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly);
                foreach (var f in files)
                {
                    var name = Path.GetFileName(f).ToLowerInvariant();
                    foreach (var sig in acSignatures)
                    {
                        if (name.Contains(sig))
                        {
                            detected = true;
                            break;
                        }
                    }
                    if (detected) break;
                }

                HasAntiCheatWarning = detected;
            }
            catch
            {
                HasAntiCheatWarning = false;
            }
        }

        public string GenerateIniContent()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[Pacing]");
            sb.AppendLine($"enablePacing={(PacingEnabled ? 1 : 0)}");
            sb.AppendLine($"enableAdvancedPacing={(EnableAdvancedPacing ? 1 : 0)}");
            sb.AppendLine($"frameGenMultiplier={FrameGenMultiplier}");
            sb.AppendLine($"targetFps={TargetFpsCap}");
            sb.AppendLine($"latencyTolerance={LatencyTolerance}");
            sb.AppendLine($"spinWaitThreshold={SpinWaitThreshold}");
            sb.AppendLine($"maxDriftCorrection={MaxDriftCorrection}");
            sb.AppendLine($"enableFastPathPresent={(EnableFastPathPresent ? 1 : 0)}");
            sb.AppendLine($"enableReflexSpoof={(EnableReflexSpoof ? 1 : 0)}");
            sb.AppendLine($"halfIntervalCadence={(HalfIntervalCadenceEnabled ? 1 : 0)}");
            sb.AppendLine($"antiLag2={(AntiLag2Enabled ? 1 : 0)}");
            sb.AppendLine($"hudProtection={(HudProtection ? 1 : 0)}");
            sb.AppendLine($"emaAlpha={EmaAlpha}");
            sb.AppendLine($"forceFlipDiscard={(ForceFlipDiscard ? 1 : 0)}");
            sb.AppendLine($"maxFrameLatency={MaxFrameLatency}");
            sb.AppendLine($"spinYieldUs={SpinYieldMicroseconds}");
            sb.AppendLine();
            sb.AppendLine("[FSR]");
            sb.AppendLine($"nativeAA={(NativeAA ? 1 : 0)}");
            sb.AppendLine($"autoLODBias={(AutoLODBias ? 1 : 0)}");
            sb.AppendLine($"textureLODBias={TextureLODBias}");
            sb.AppendLine($"enableRCAS={(EnableRCASOverride ? 1 : 0)}");
            sb.AppendLine($"rcasSharpness={Sharpness}");
            sb.AppendLine($"reactiveMask={(ReactiveMask ? 1 : 0)}");
            sb.AppendLine($"reactiveMaskSensitivity={ReactiveMaskSensitivity}");
            sb.AppendLine($"clampMinRenderScale={(ClampMinRenderScale ? 1 : 0)}");
            sb.AppendLine($"driverFgMultiplier={DriverFgMultiplier}");
            sb.AppendLine();
            sb.AppendLine("[RayRegen]");
            sb.AppendLine($"enableRayRegen={(RayRegenEnabled ? 1 : 0)}");
            sb.AppendLine($"checkerboardRayRecon={(CheckerboardRayRecon ? 1 : 0)}");
            sb.AppendLine($"waveletBilateralNormalFilter={(WaveletBilateralNormalFilter ? 1 : 0)}");
            sb.AppendLine($"directComputeNrcLatch={(DirectComputeNrcLatch ? 1 : 0)}");
            sb.AppendLine($"neuralRadianceCache={(NeuralRadianceCache ? 1 : 0)}");
            sb.AppendLine($"denoiseReflections={(DenoiseReflections ? 1 : 0)}");
            sb.AppendLine($"denoiseShadows={(DenoiseShadows ? 1 : 0)}");
            sb.AppendLine($"glossyFilter={(GlossyRadianceFilter ? 1 : 0)}");
            sb.AppendLine($"colorSpaceCorrect={(ColorSpaceCorrect ? 1 : 0)}");
            sb.AppendLine($"spatialFilterPasses={SpatialFilterPasses}");
            sb.AppendLine($"temporalWeight={TemporalWeight}");
            sb.AppendLine($"roughnessThreshold={RoughnessThreshold}");
            sb.AppendLine($"depthSigma={DepthSigma}");
            sb.AppendLine($"normalSigma={NormalSigma}");
            sb.AppendLine($"forceAutoExposure={(ForceAutoExposure ? 1 : 0)}");
            sb.AppendLine($"disocclusionFilter={(DisocclusionFilterEnabled ? 1 : 0)}");
            return sb.ToString();
        }

        public void WriteToPublicIni()
        {
            if (string.IsNullOrWhiteSpace(InstallDirectory) || !Directory.Exists(InstallDirectory))
                return;

            string iniPath = Path.Combine(InstallDirectory, "aetherpulse.ini");
            File.WriteAllText(iniPath, GenerateIniContent());
        }

        public bool Equals(GameProfile? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            if (!string.IsNullOrEmpty(Id) && !string.IsNullOrEmpty(other.Id))
                return string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase);
            return string.Equals(GameName, other.GameName, StringComparison.OrdinalIgnoreCase);
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public bool HasBackup
        {
            get
            {
                if (string.IsNullOrWhiteSpace(ExecutablePath)) return false;
                string gameDir = AppUI.Services.GameBackupService.ResolveGameDirectory(ExecutablePath);
                return AppUI.Services.GameBackupService.HasExistingBackup(gameDir);
            }
        }

        public void RefreshBackupStatus()
        {
            OnPropertyChanged(nameof(HasBackup));
            OnPropertyChanged(nameof(IsProxySelectionEnabled));
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public static string[] AvailableProxyNames { get; } = new[]
        {
            "dxgi.dll",
            "version.dll",
            "d3d12.dll"
        };

        private string _selectedProxyName = "dxgi.dll";
        public string SelectedProxyName
        {
            get => string.IsNullOrWhiteSpace(_selectedProxyName) ? "dxgi.dll" : _selectedProxyName;
            set
            {
                if (SetProperty(ref _selectedProxyName, value))
                {
                    OnPropertyChanged(nameof(ProxyDisplayLabel));
                }
            }
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsProxySelectionEnabled => !HasBackup;

        public override bool Equals(object? obj)
        {
            return Equals(obj as GameProfile);
        }

        public override int GetHashCode()
        {
            return !string.IsNullOrEmpty(Id)
                ? StringComparer.OrdinalIgnoreCase.GetHashCode(Id)
                : StringComparer.OrdinalIgnoreCase.GetHashCode(GameName ?? string.Empty);
        }
    }
}





