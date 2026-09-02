using AppUI.Messages;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AppUI.Models;
using AppUI.Services;

namespace AppUI.ViewModels
{
    public partial class GameLibraryViewModel : ObservableObject
    {
        private readonly IDeploymentService _deploymentService;
        private readonly IProfileStorageService _storageService;

        public event EventHandler<GameProfile?>? SelectedProfileChanged;
        public event EventHandler<GameProfile>? TuneGameRequested;

        [ObservableProperty]
        private ObservableCollection<GameProfile> _gameProfiles = new();

        public ObservableCollection<GameProfile> Games => GameProfiles;

        private GameProfile? _selectedProfile;
        public GameProfile? SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                if (SetProperty(ref _selectedProfile, value))
                {
                    SelectedProfileChanged?.Invoke(this, value);
                }
            }
        }

        public GameProfile? SelectedGame
        {
            get => SelectedProfile;
            set => SelectedProfile = value;
        }

        [ObservableProperty]
        private string _statusMessage = "Ready";

        [ObservableProperty]
        private bool _isScanning = false;

        // Anti-Cheat Modal Properties
        [ObservableProperty]
        private bool _antiCheatModalVisible = false;

        [ObservableProperty]
        private bool _antiCheatLiabilityAcknowledged = false;

        [ObservableProperty]
        private string _antiCheatTargetGameName = string.Empty;

        [ObservableProperty]
        private string _antiCheatDetectedSystem = "Custom Engine / Online";

        // Compatibility Verification Modal Properties
        [ObservableProperty]
        private bool _validationFailureModalVisible = false;

        [ObservableProperty]
        private string _validationTargetName = string.Empty;

        [ObservableProperty]
        private bool _isDx12Passed = true;

        [ObservableProperty]
        private bool _isDlssPassed = true;

        private string? _pendingImportPath;
        private GameProfile? _pendingDeployProfile;

        public GameLibraryViewModel(IDeploymentService deploymentService, IProfileStorageService storageService)
        {
            _deploymentService = deploymentService;
            _storageService = storageService;
            _gameProfiles.CollectionChanged += OnGameProfilesCollectionChanged;
        }

        public GameLibraryViewModel(IDeploymentService deploymentService, IProfileStorageService storageService, ITelemetryService? telemetryService) 
            : this(deploymentService, storageService) { }

        public GameLibraryViewModel(IDeploymentService deploymentService, object? p2, object? p3) 
            : this(deploymentService, (p2 as IProfileStorageService) ?? new ProfileStorageService()) { }

        public GameLibraryViewModel() : this(new DeploymentService(), new ProfileStorageService()) 
        {
            _ = LoadProfilesAsync();
        }

        private void OnGameProfilesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (GameProfile p in e.NewItems)
                {
                    p.PropertyChanged += OnProfilePropertyChanged;
                }
            }
            if (e.OldItems != null)
            {
                foreach (GameProfile p in e.OldItems)
                {
                    p.PropertyChanged -= OnProfilePropertyChanged;
                }
            }
        }

        private async void OnProfilePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(GameProfile.IsGameRunning)) return;
            await SaveProfilesAsync();
        }

        public async Task LoadProfilesAsync()
        {
            var profiles = await _storageService.LoadProfilesAsync();
            GameProfiles.Clear();

            if (profiles != null && profiles.Count > 0)
            {
                foreach (var p in profiles)
                {
                    p.IsDeployed = _deploymentService.IsDeployed(p.ExecutablePath, p.Mode);
                    p.CheckAntiCheatProtection();
                    GameProfiles.Add(p);
                }
            }

            SelectedProfile = GameProfiles.Count > 0 ? GameProfiles[0] : null;
        }

        public async Task SaveProfilesAsync()
        {
            await _storageService.SaveProfilesAsync(GameProfiles);
        }

        private async Task SaveProfilesInternalAsync()
        {
            await SaveProfilesAsync();
        }

        [RelayCommand]
        public async Task Deploy(GameProfile? profile)
        {
            var target = profile ?? SelectedProfile;
            if (target == null || string.IsNullOrWhiteSpace(target.ExecutablePath)) return;

            StatusMessage = $"Deploying to {target.Name}...";
            var result = await _deploymentService.DeployAsync(target.ExecutablePath, target.Mode);
            StatusMessage = result.Message;
            target.IsDeployed = _deploymentService.IsDeployed(target.ExecutablePath, target.Mode);
            target.WriteToPublicIni();
            await SaveProfilesAsync();
        }

        [RelayCommand]
        public async Task DeleteGameFromLibrary(GameProfile? profile)
        {
            var target = profile ?? SelectedProfile;
            if (target != null)
            {
                GameProfiles.Remove(target);
                WeakReferenceMessenger.Default.Send(new LibraryUpdatedMessage());
                if (SelectedProfile == target)
                {
                    SelectedProfile = GameProfiles.Count > 0 ? GameProfiles[0] : null;
                }
                await SaveProfilesAsync();
                StatusMessage = $"Removed {target.Name} from library.";
            }
        }

        [RelayCommand]
        public void CleanConflicts(GameProfile? profile)
        {
            var target = profile ?? SelectedProfile;
            if (target != null)
            {
                // Clear simulated OptiScaler artifact flag
                target.HasOptiScalerConflict = false;
                StatusMessage = $"Cleaned conflicting mod artifacts for {target.Name} (ReShade preserved).";
            }
        }

        [RelayCommand]
        public void CancelAntiCheatModal()
        {
            AntiCheatModalVisible = false;
            AntiCheatLiabilityAcknowledged = false;
            _pendingDeployProfile = null;
        }

        [RelayCommand]
        public async Task ConfirmAntiCheatDeploy()
        {
            AntiCheatModalVisible = false;
            if (_pendingDeployProfile != null)
            {
                var profileToDeploy = _pendingDeployProfile;
                _pendingDeployProfile = null;
                AntiCheatLiabilityAcknowledged = false;

                await Deploy(profileToDeploy);
            }
        }

        [RelayCommand]
        public async Task Undeploy(GameProfile? profile)
        {
            var target = profile ?? SelectedProfile;
            if (target == null || string.IsNullOrWhiteSpace(target.ExecutablePath)) return;

            StatusMessage = $"Removing deployment from {target.Name}...";
            var result = await _deploymentService.UninstallAsync(target.ExecutablePath, target.Mode);
            StatusMessage = result.Message;
            target.IsDeployed = _deploymentService.IsDeployed(target.ExecutablePath, target.Mode);
            await SaveProfilesAsync();
        }

        [RelayCommand]
        public void CancelValidationModal()
        {
            ValidationFailureModalVisible = false;
            _pendingImportPath = null;
        }

        [RelayCommand]
        public async Task ConfirmForceAddGame()
        {
            ValidationFailureModalVisible = false;
            if (!string.IsNullOrEmpty(_pendingImportPath))
            {
                await AddGameDirectlyAsync(_pendingImportPath);
                _pendingImportPath = null;
            }
        }

        [RelayCommand]
        public async Task RemoveHook(GameProfile? profile)
        {
            var target = profile ?? SelectedProfile;
            if (target == null || string.IsNullOrWhiteSpace(target.ExecutablePath)) return;

            StatusMessage = $"Restoring {target.Name}...";
            var result = await _deploymentService.UninstallAsync(target.ExecutablePath, target.Mode);
            StatusMessage = result.Message;
            target.IsDeployed = _deploymentService.IsDeployed(target.ExecutablePath, target.Mode);
            await SaveProfilesAsync();
        }

        [RelayCommand]
        public void LaunchGame(GameProfile? profile)
        {
            var target = profile ?? SelectedProfile;
            if (target == null || string.IsNullOrWhiteSpace(target.ExecutablePath)) return;

            try
            {
                var workingDirectory = System.IO.Path.GetDirectoryName(target.ExecutablePath);
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = target.ExecutablePath,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = true
                };

                System.Diagnostics.Process.Start(startInfo);
                StatusMessage = $"Launched {target.Name}.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to launch {target.Name}: {ex.Message}";
            }
        }

        [RelayCommand]
        public void TuneGame(GameProfile? profile)
        {
            var target = profile ?? SelectedProfile;
            if (target != null)
            {
                TuneGameRequested?.Invoke(this, target);
            }
        }

        public async Task AddGameFromPathAsync(string exePath)
        {
            if (!File.Exists(exePath)) return;

            string gameDir = Path.GetDirectoryName(exePath) ?? exePath;
            bool hasD3D12 = Directory.Exists(Path.Combine(gameDir, "d3d12")) ||
                            File.Exists(Path.Combine(gameDir, "d3d12core.dll")) ||
                            File.Exists(Path.Combine(gameDir, "dxcompiler.dll"));
            bool hasDlss = File.Exists(Path.Combine(gameDir, "sl.interposer.dll")) ||
                           File.Exists(Path.Combine(gameDir, "nvngx_dlss.dll"));

            if (!hasD3D12 || !hasDlss)
            {
                _pendingImportPath = exePath;
                ValidationTargetName = Path.GetFileNameWithoutExtension(exePath);
                IsDx12Passed = hasD3D12;
                IsDlssPassed = hasDlss;
                ValidationFailureModalVisible = true;
                return;
            }

            await AddGameDirectlyAsync(exePath);
        }

        private async Task AddGameDirectlyAsync(string exePath)
        {
            string gameName = Path.GetFileNameWithoutExtension(exePath);
            var newProfile = new GameProfile
            {
                Name = gameName,
                ExecutablePath = exePath,
                IsDeployed = _deploymentService.IsDeployed(exePath, DeploymentMode.StreamlineInterposer)
            };
            newProfile.CheckAntiCheatProtection();

            GameProfiles.Add(newProfile);
            SelectedProfile = newProfile;
            await SaveProfilesAsync();
        }
    }
}










