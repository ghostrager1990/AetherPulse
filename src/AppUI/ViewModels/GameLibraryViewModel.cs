using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AppUI.Models;
using AppUI.Services;
using AppUI.Views;

namespace AppUI.ViewModels
{
    public partial class GameLibraryViewModel : ObservableObject
    {
        private readonly IDeploymentService _deploymentService;
        private readonly IProfileStorageService _storageService;
        private readonly IConflictDetectorService _conflictDetector;
        private readonly ITelemetryService? _telemetryService;

        public event EventHandler<GameProfile?>? SelectedProfileChanged;
        public event EventHandler<GameProfile>? TuneGameRequested;

        [ObservableProperty]
        private ObservableCollection<GameProfile> _gameProfiles = new();

        [ObservableProperty]
        private GameProfile? _selectedGameProfile;

        [ObservableProperty]
        private bool _isScanning;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private string _searchFilter = string.Empty;

        public GameLibraryViewModel(
            IDeploymentService deploymentService,
            IProfileStorageService storageService,
            ITelemetryService? telemetryService = null,
            IConflictDetectorService? conflictDetector = null)
        {
            _deploymentService = deploymentService;
            _storageService = storageService;
            _telemetryService = telemetryService;
            _conflictDetector = conflictDetector ?? new ConflictDetectorService();
        }

        [ObservableProperty]
        private GameCapabilityInfo? _selectedGameCapability;

        partial void OnSelectedGameProfileChanged(GameProfile? value)
        {
            if (value != null)
            {
                value.RefreshCapability();
                SelectedGameCapability = value.Capability;
                CheckProfileConflicts(value);
            }
            else
            {
                SelectedGameCapability = null;
            }
            SelectedProfileChanged?.Invoke(this, value);
        }

        public void CheckProfileConflicts(GameProfile? profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.InstallDirectory)) return;
            var scan = _conflictDetector.ScanForConflicts(profile.InstallDirectory);
            profile.HasOptiScalerConflict = scan.HasOptiScalerConflict;
            profile.OptiScalerWarningMessage = scan.WarningMessage;
        }

        [RelayCommand]
        public void CleanConflicts(GameProfile? profile)
        {
            var target = profile ?? SelectedGameProfile;
            if (target == null || string.IsNullOrWhiteSpace(target.InstallDirectory)) return;

            if (_conflictDetector.CleanOptiScalerArtifacts(target.InstallDirectory, out var removed))
            {
                target.HasOptiScalerConflict = false;
                target.OptiScalerWarningMessage = string.Empty;
                StatusMessage = $"Cleaned {removed.Count} OptiScaler conflicting mod file(s).";
            }
        }

        [RelayCommand]
        public void TuneGame(GameProfile? profile)
        {
            var target = profile ?? SelectedGameProfile;
            if (target != null)
            {
                SelectedGameProfile = target;
                TuneGameRequested?.Invoke(this, target);
            }
        }

        [RelayCommand]
        public async Task LoadProfilesAsync()
        {
            var loaded = await _storageService.LoadProfilesAsync();
            GameProfiles.Clear();

            bool purgedAny = false;

            foreach (var profile in loaded)
            {
                if (IsExcludedExecutable(profile.ExecutablePath))
                {
                    purgedAny = true;
                    continue;
                }

                if (profile.Mode == DeploymentMode.VersionProxy || profile.Mode == DeploymentMode.Both)
                {
                    profile.Mode = DeploymentMode.DxcoreProxy;
                }

                if (!string.IsNullOrEmpty(profile.ExecutablePath) && File.Exists(profile.ExecutablePath))
                {
                    profile.RefreshCapability();
                    var exeDir = Path.GetDirectoryName(profile.ExecutablePath)!;
                    bool hasShim = File.Exists(Path.Combine(exeDir, "version.dll")) && File.Exists(Path.Combine(exeDir, "AetherPulseCore.dll"));
                    profile.IsHookDeployed = hasShim || _deploymentService.IsDeployed(exeDir, profile.Mode);
                    profile.DeploymentStatus = profile.IsHookDeployed ? "Active" : "Inactive";
                    CheckProfileConflicts(profile);
                }
                GameProfiles.Add(profile);
            }

            if (purgedAny)
            {
                await SaveProfilesAsync();
            }

            if (GameProfiles.Any() && SelectedGameProfile == null)
            {
                SelectedGameProfile = GameProfiles.First();
            }
        }

        [ObservableProperty]
        private bool _validationFailureModalVisible;

        [ObservableProperty]
        private string _validationTargetName = string.Empty;

        [ObservableProperty]
        private string _pendingExecutablePath = string.Empty;

        [ObservableProperty]
        private bool _isDx12Passed;

        [ObservableProperty]
        private bool _isDlssPassed;

        [ObservableProperty]
        private string _validationWarningMessage = string.Empty;

        [ObservableProperty]
        private bool _antiCheatModalVisible;

        [ObservableProperty]
        private string _antiCheatTargetGameName = string.Empty;

        [ObservableProperty]
        private string _antiCheatDetectedSystem = string.Empty;

        [ObservableProperty]
        private bool _antiCheatLiabilityAcknowledged;

        [ObservableProperty]
        private GameProfile? _pendingAntiCheatDeployProfile;

        [RelayCommand]
        public async Task SaveProfilesAsync()
        {
            await _storageService.SaveProfilesAsync(GameProfiles);
        }

        [RelayCommand]
        public async Task AddGameFromPathAsync(string executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                StatusMessage = "Invalid executable path.";
                return;
            }

            var existing = GameProfiles.FirstOrDefault(p =>
                string.Equals(p.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                SelectedGameProfile = existing;
                StatusMessage = $"'{existing.GameName}' is already in your library.";
                return;
            }

            var validation = GameValidationService.ValidateGameExecutable(executablePath);
            if (!validation.IsFullyCompatible)
            {
                ValidationTargetName = Path.GetFileName(executablePath);
                PendingExecutablePath = executablePath;
                IsDx12Passed = validation.IsDx12Supported;
                IsDlssPassed = validation.HasDlssOrStreamline;
                ValidationWarningMessage = validation.FailureReason;
                ValidationFailureModalVisible = true;
                return;
            }

            await CompleteAddGameAsync(executablePath);
        }

        [RelayCommand]
        public async Task ConfirmForceAddGameAsync()
        {
            ValidationFailureModalVisible = false;
            if (!string.IsNullOrEmpty(PendingExecutablePath))
            {
                await CompleteAddGameAsync(PendingExecutablePath);
                PendingExecutablePath = string.Empty;
            }
        }

        [RelayCommand]
        public void CancelValidationModal()
        {
            ValidationFailureModalVisible = false;
            PendingExecutablePath = string.Empty;
            StatusMessage = "Game import cancelled.";
        }

        [RelayCommand]
        public async Task ConfirmAntiCheatDeployAsync()
        {
            if (!AntiCheatLiabilityAcknowledged || PendingAntiCheatDeployProfile == null)
            {
                return;
            }

            var target = PendingAntiCheatDeployProfile;
            AntiCheatModalVisible = false;
            PendingAntiCheatDeployProfile = null;

            await ExecuteDeployInternalAsync(target);
        }

        [RelayCommand]
        public void CancelAntiCheatModal()
        {
            AntiCheatModalVisible = false;
            PendingAntiCheatDeployProfile = null;
            AntiCheatLiabilityAcknowledged = false;
            StatusMessage = "Deployment cancelled: Anti-Cheat hazard avoided.";
        }

        private async Task CompleteAddGameAsync(string executablePath)
        {
            string installDir = Path.GetDirectoryName(executablePath)!;
            string gameName = Path.GetFileNameWithoutExtension(executablePath);

            try
            {
                var vi = FileVersionInfo.GetVersionInfo(executablePath);
                if (!string.IsNullOrWhiteSpace(vi.ProductName) &&
                    !vi.ProductName.Contains("Setup", StringComparison.OrdinalIgnoreCase) &&
                    !vi.ProductName.Contains("Installer", StringComparison.OrdinalIgnoreCase) &&
                    vi.ProductName.Length > 2)
                {
                    gameName = vi.ProductName.Trim();
                }
            }
            catch
            {
            }

            var ignoredPaths = await _storageService.LoadIgnoredGamesAsync();
            if (ignoredPaths.Remove(executablePath))
            {
                await _storageService.SaveIgnoredGamesAsync(ignoredPaths);
            }

            var acScan = AntiCheatDetectionService.ScanGame(executablePath, installDir);

            var profile = new GameProfile
            {
                GameName = gameName,
                ExecutablePath = executablePath,
                InstallDirectory = installDir,
                Mode = DeploymentMode.DxcoreProxy,
                IsHookDeployed = _deploymentService.IsDeployed(installDir, DeploymentMode.DxcoreProxy),
                HasAntiCheatWarning = acScan.IsOnlineOrProtectedGame,
                DetectedAntiCheatName = acScan.DetectedSystem
            };

            GameProfiles.Add(profile);
            SelectedGameProfile = profile;

            await SaveProfilesAsync();
            StatusMessage = $"Added '{gameName}' to library.";
        }

        [RelayCommand]
        public async Task AutoScanGamesAsync()
        {
            IsScanning = true;
            StatusMessage = "Scanning DirectX 12 & Vulkan libraries for games...";

            int foundCount = 0;
            var ignoredPaths = await _storageService.LoadIgnoredGamesAsync();
            var ignoredCandidates = new List<GameProfile>();

            try
            {
                await Task.Run(() =>
                {
                    var searchPaths = GetCommonGameDirectories();

                    foreach (var baseDir in searchPaths)
                    {
                        if (string.IsNullOrEmpty(baseDir) || !Directory.Exists(baseDir)) continue;

                        try
                        {
                            var subDirs = Directory.GetDirectories(baseDir);
                            foreach (var dir in subDirs)
                            {
                                try
                                {
                                    var exeFiles = Directory.GetFiles(dir, "*.exe", SearchOption.TopDirectoryOnly);
                                    foreach (var fullExePath in exeFiles)
                                    {
                                        string exeName = Path.GetFileName(fullExePath);
                                        if (IsExcludedExecutable(fullExePath)) continue;

                                        if (GameProfiles.Any(p => string.Equals(p.ExecutablePath, fullExePath, StringComparison.OrdinalIgnoreCase)))
                                        {
                                            continue;
                                        }

                                        try
                                        {
                                            string gameName = Path.GetFileName(dir);
                                            try
                                            {
                                                var versionInfo = FileVersionInfo.GetVersionInfo(fullExePath);
                                                if (!string.IsNullOrWhiteSpace(versionInfo.ProductName) &&
                                                    !versionInfo.ProductName.Contains("Setup", StringComparison.OrdinalIgnoreCase) &&
                                                    !versionInfo.ProductName.Contains("Installer", StringComparison.OrdinalIgnoreCase) &&
                                                    versionInfo.ProductName.Length > 2)
                                                {
                                                    gameName = versionInfo.ProductName.Trim();
                                                }
                                            }
                                            catch
                                            {
                                            }

                                            var profile = new GameProfile
                                            {
                                                GameName = gameName,
                                                ExecutablePath = fullExePath,
                                                InstallDirectory = dir,
                                                Mode = DeploymentMode.DxcoreProxy,
                                                IsHookDeployed = _deploymentService.IsDeployed(dir, DeploymentMode.DxcoreProxy)
                                            };

                                            if (ignoredPaths.Contains(fullExePath))
                                            {
                                                ignoredCandidates.Add(profile);
                                                continue;
                                            }

                                            if (Application.Current?.Dispatcher != null)
                                            {
                                                Application.Current.Dispatcher.Invoke(() =>
                                                {
                                                    GameProfiles.Add(profile);
                                                });
                                            }
                                            else
                                            {
                                                GameProfiles.Add(profile);
                                            }

                                            foundCount++;
                                        }
                                        catch
                                        {
                                        }
                                    }
                                }
                                catch
                                {
                                }
                            }
                        }
                        catch
                        {
                        }
                    }
                });

                await SaveProfilesAsync();

                if (ignoredCandidates.Any() && Application.Current?.Dispatcher != null)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var restoreVm = new RestoreGamesViewModel(ignoredCandidates);
                        var dialog = new RestoreGamesDialog(restoreVm)
                        {
                            Owner = Application.Current.MainWindow
                        };

                        if (dialog.ShowDialog() == true)
                        {
                            var restored = restoreVm.GetSelectedProfiles();
                            foreach (var p in restored)
                            {
                                GameProfiles.Add(p);
                                ignoredPaths.Remove(p.ExecutablePath);
                                foundCount++;
                            }
                            _ = _storageService.SaveIgnoredGamesAsync(ignoredPaths);
                            _ = SaveProfilesAsync();
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Scan error: {ex.Message}";
            }
            finally
            {
                IsScanning = false;
            }

            StatusMessage = foundCount > 0
                ? $"Scan complete: Found and added {foundCount} DX12/Vulkan games."
                : "Scan complete: No new DX12/Vulkan games found.";
        }

        [RelayCommand]
        public async Task DeployAsync(GameProfile? profile)
        {
            var target = profile ?? SelectedGameProfile;
            if (target == null || string.IsNullOrWhiteSpace(target.ExecutablePath))
            {
                StatusMessage = "No game selected for deployment.";
                return;
            }

            var acScan = AntiCheatDetectionService.ScanGame(target.ExecutablePath, target.InstallDirectory);
            if (acScan.IsOnlineOrProtectedGame || target.HasAntiCheatWarning)
            {
                AntiCheatTargetGameName = target.GameName;
                AntiCheatDetectedSystem = !string.IsNullOrEmpty(target.DetectedAntiCheatName) ? target.DetectedAntiCheatName : acScan.DetectedSystem;
                AntiCheatLiabilityAcknowledged = false;
                PendingAntiCheatDeployProfile = target;
                AntiCheatModalVisible = true;
                return;
            }

            await ExecuteDeployInternalAsync(target);
        }

        private async Task ExecuteDeployInternalAsync(GameProfile target)
        {
            try
            {
                string targetDir = !string.IsNullOrWhiteSpace(target.ExecutablePath) && File.Exists(target.ExecutablePath)
                    ? Path.GetDirectoryName(target.ExecutablePath)!
                    : (!string.IsNullOrWhiteSpace(target.InstallDirectory) ? target.InstallDirectory : AppContext.BaseDirectory);

                string baseDir = AppContext.BaseDirectory;
                string nativeCoreBuildDir = @"G:\Antigravity Projects\AetherPulse\src\NativeCore\build\Release";

                string versionSrc = File.Exists(Path.Combine(baseDir, "version.dll"))
                    ? Path.Combine(baseDir, "version.dll")
                    : (File.Exists(Path.Combine(baseDir, "Redist", "version.dll"))
                        ? Path.Combine(baseDir, "Redist", "version.dll")
                        : Path.Combine(nativeCoreBuildDir, "version.dll"));

                string coreSrc = File.Exists(Path.Combine(baseDir, "AetherPulseCore.dll"))
                    ? Path.Combine(baseDir, "AetherPulseCore.dll")
                    : (File.Exists(Path.Combine(baseDir, "Redist", "AetherPulseCore.dll"))
                        ? Path.Combine(baseDir, "Redist", "AetherPulseCore.dll")
                        : Path.Combine(nativeCoreBuildDir, "AetherPulseCore.dll"));

                if (File.Exists(versionSrc)) File.Copy(versionSrc, Path.Combine(targetDir, "version.dll"), true);
                if (File.Exists(coreSrc)) File.Copy(coreSrc, Path.Combine(targetDir, "AetherPulseCore.dll"), true);

                string sdkSource = Path.Combine(baseDir, "payload", "sdk");
                if (!Directory.Exists(sdkSource) || !Directory.GetFiles(sdkSource, "*.dll").Any())
                {
                    sdkSource = @"G:\Antigravity Projects\AetherPulse\src\AppUI\Assets\Payload";
                }

                if (Directory.Exists(sdkSource))
                {
                    string targetPayloadSdkDir = Path.Combine(targetDir, "payload", "sdk");
                    if (!Directory.Exists(targetPayloadSdkDir))
                    {
                        Directory.CreateDirectory(targetPayloadSdkDir);
                    }

                    foreach (var file in Directory.GetFiles(sdkSource, "*.dll"))
                    {
                        string fileName = Path.GetFileName(file);
                        File.Copy(file, Path.Combine(targetDir, fileName), true);
                        File.Copy(file, Path.Combine(targetPayloadSdkDir, fileName), true);

                        if (fileName.Equals("amd_fidelityfx_loader_dx12.dll", StringComparison.OrdinalIgnoreCase))
                        {
                            File.Copy(file, Path.Combine(targetDir, "amd_fidelityfx_dx12.dll"), true);
                        }
                    }
                }

                IniConfigService.SaveConfigToPath(Path.Combine(targetDir, "aetherpulse.ini"), target);

                target.IsHookDeployed = File.Exists(Path.Combine(targetDir, "version.dll")) && File.Exists(Path.Combine(targetDir, "AetherPulseCore.dll"));
                target.DeploymentStatus = target.IsHookDeployed ? "Active" : "Inactive";
                target.LastDeployedAt = DateTime.Now;

                StatusMessage = $"Deployed AetherPulse (version.dll + Core + SDK Payload) for {target.GameName}.";
                await SaveProfilesAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Deployment error: {ex.Message}";
            }
        }

        [RelayCommand]
        public async Task RemoveHookAsync(GameProfile? profile)
        {
            var target = profile ?? SelectedGameProfile;
            if (target == null || string.IsNullOrWhiteSpace(target.ExecutablePath))
            {
                StatusMessage = "No game selected.";
                return;
            }

            try
            {
                var targetDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(target.ExecutablePath) && File.Exists(target.ExecutablePath))
                {
                    targetDirs.Add(Path.GetDirectoryName(target.ExecutablePath)!);
                }
                if (!string.IsNullOrWhiteSpace(target.InstallDirectory) && Directory.Exists(target.InstallDirectory))
                {
                    targetDirs.Add(target.InstallDirectory);
                }

                string[] filesToDelete = new[]
                {
                    "aetherpulse.ini",
                    "version.dll",
                    "AetherPulseCore.dll"                    
                };

                foreach (var dir in targetDirs)
                {
                    await _deploymentService.UninstallAsync(dir, target.Mode);

                    foreach (var fileName in filesToDelete)
                    {
                        string filePath = Path.Combine(dir, fileName);
                        if (File.Exists(filePath))
                        {
                            DeleteFileWithRetry(filePath);
                        }
                    }

                    string payloadDir = Path.Combine(dir, "payload");
                    if (Directory.Exists(payloadDir))
                    {
                        DeleteDirectoryWithRetry(payloadDir);
                    }
                }

                target.IsHookDeployed = false;
                target.DeploymentStatus = "Inactive";
                StatusMessage = $"Uninstalled all AetherPulse hook files, payload, and config from {target.GameName}.";
                await SaveProfilesAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Uninstall error: {ex.Message}";
            }
        }

        private static void DeleteFileWithRetry(string path, int maxAttempts = 5)
        {
            for (int i = 0; i < maxAttempts; i++)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.SetAttributes(path, FileAttributes.Normal);
                        File.Delete(path);
                    }
                    return;
                }
                catch
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    Thread.Sleep(50);
                }
            }
        }

        private static void DeleteDirectoryWithRetry(string path, int maxAttempts = 5)
        {
            for (int i = 0; i < maxAttempts; i++)
            {
                try
                {
                    if (Directory.Exists(path))
                    {
                        Directory.Delete(path, recursive: true);
                    }
                    return;
                }
                catch
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    Thread.Sleep(50);
                }
            }
        }

        [RelayCommand]
        public async Task DeleteGameFromLibraryAsync(GameProfile? profile)
        {
            var target = profile ?? SelectedGameProfile;
            if (target == null) return;

            var ignored = await _storageService.LoadIgnoredGamesAsync();
            ignored.Add(target.ExecutablePath);
            await _storageService.SaveIgnoredGamesAsync(ignored);

            GameProfiles.Remove(target);

            if (SelectedGameProfile == target)
            {
                SelectedGameProfile = GameProfiles.FirstOrDefault();
            }

            await SaveProfilesAsync();
            StatusMessage = $"Removed '{target.GameName}' from library and ignored in future scans.";
        }

        [RelayCommand]
        public void LaunchGame(GameProfile? profile)
        {
            var target = profile ?? SelectedGameProfile;
            if (target == null || !File.Exists(target.ExecutablePath))
            {
                StatusMessage = "Cannot launch: Executable not found.";
                return;
            }

            if (HasAntiCheatIndicator(target.InstallDirectory))
            {
                bool proceed = false;
                if (Application.Current?.Dispatcher != null)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var warningDialog = new AntiCheatWarningDialog
                        {
                            Owner = Application.Current.MainWindow
                        };
                        proceed = warningDialog.ShowDialog() == true;
                    });
                }

                if (!proceed)
                {
                    StatusMessage = "Launch cancelled due to Anti-Cheat detection.";
                    return;
                }
            }

            try
            {
                _telemetryService?.SetActiveTarget(target.GameName, target.ExecutablePath);

                var startInfo = new ProcessStartInfo
                {
                    FileName = target.ExecutablePath,
                    WorkingDirectory = Path.GetDirectoryName(target.ExecutablePath),
                    UseShellExecute = true
                };
                Process.Start(startInfo);
                StatusMessage = $"Launched {target.GameName}.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to launch game: {ex.Message}";
            }
        }

        public static bool HasAntiCheatIndicator(string installDir)
        {
            if (string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir)) return false;

            try
            {
                string[] files = Directory.GetFileSystemEntries(installDir, "*", SearchOption.TopDirectoryOnly);
                foreach (var f in files)
                {
                    string name = Path.GetFileName(f).ToLowerInvariant();
                    if (name.Contains("easyanticheat") ||
                        name.Contains("battleye") ||
                        name.Contains("vanguard") ||
                        name.Contains("ricochet") ||
                        name.Contains("punkbuster") ||
                        name.Contains("eac_") ||
                        name.Contains("bedaisy") ||
                        name.Contains("beservice") ||
                        name.Contains("vgc") ||
                        name.Contains("vgk"))
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool IsDirectX12OrVulkanExecutable(string exePath, string installDir)
        {
            try
            {
                var fileInfo = new FileInfo(exePath);
                if (fileInfo.Length < (long)(1.5 * 1024 * 1024)) return false;

                if (Directory.Exists(installDir))
                {
                    var dirFiles = Directory.GetFiles(installDir, "*.dll", SearchOption.TopDirectoryOnly)
                        .Select(f => Path.GetFileName(f).ToLowerInvariant())
                        .ToHashSet();

                    if (dirFiles.Contains("sl.interposer.dll") ||
                        dirFiles.Contains("nvngx.dll") ||
                        dirFiles.Contains("nvngx_dlss.dll") ||
                        dirFiles.Contains("nvngx_dlss_d.dll") ||
                        dirFiles.Contains("dxil.dll") ||
                        dirFiles.Contains("d3d12.dll") ||
                        dirFiles.Contains("d3d12core.dll") ||
                        dirFiles.Contains("vulkan-1.dll"))
                    {
                        return true;
                    }
                }

                using var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (fs.Length < 0x40) return false;

                using var reader = new BinaryReader(fs);
                ushort dosMagic = reader.ReadUInt16();
                if (dosMagic != 0x5A4D) return false;

                fs.Seek(0x3C, SeekOrigin.Begin);
                int peOffset = reader.ReadInt32();

                if (peOffset <= 0 || peOffset + 0x18 > fs.Length) return false;

                fs.Seek(peOffset, SeekOrigin.Begin);
                uint peSignature = reader.ReadUInt32();
                if (peSignature != 0x00004550) return false;

                ushort machine = reader.ReadUInt16();
                if (machine != 0x8664)
                {
                    return false;
                }

                int scanLength = (int)Math.Min(fs.Length, 1024 * 1024);
                fs.Seek(0, SeekOrigin.Begin);
                byte[] buffer = reader.ReadBytes(scanLength);
                string binaryString = Encoding.ASCII.GetString(buffer).ToLowerInvariant();

                if (binaryString.Contains("d3d12.dll") ||
                    binaryString.Contains("vulkan-1.dll") ||
                    binaryString.Contains("sl.interposer.dll") ||
                    binaryString.Contains("nvngx_dlss_d.dll") ||
                    (binaryString.Contains("dxgi.dll") && !binaryString.Contains("d3d9.dll")))
                {
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static List<string> GetCommonGameDirectories()
        {
            var list = new List<string>();

            string[] drives = new[] { "C", "D", "E", "F", "G" };
            foreach (var drive in drives)
            {
                list.Add($@"{drive}:\Program Files (x86)\Steam\steamapps\common");
                list.Add($@"{drive}:\SteamLibrary\steamapps\common");
                list.Add($@"{drive}:\Program Files\Epic Games");
                list.Add($@"{drive}:\Epic Games");
                list.Add($@"{drive}:\GOG Galaxy\Games");
                list.Add($@"{drive}:\GOG Games");
                list.Add($@"{drive}:\XboxGames");
                list.Add($@"{drive}:\Games");
            }

            return list;
        }

        private static bool IsExcludedExecutable(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath)) return true;

            string normalizedPath = exePath.Replace('\\', '/').ToLowerInvariant();
            string name = Path.GetFileName(exePath).ToLowerInvariant();

            if (normalizedPath.Contains("/installer/") ||
                normalizedPath.Contains("/redist/") ||
                normalizedPath.Contains("/support/") ||
                normalizedPath.Contains("/directx/") ||
                normalizedPath.Contains("/dotnet/") ||
                normalizedPath.Contains("/__installer/"))
            {
                return true;
            }

            return name.Contains("crashreport") ||
                   name.Contains("unins") ||
                   name.Contains("setup") ||
                   name.Contains("launcher") ||
                   name.Contains("helper") ||
                   name.Contains("easyanticheat") ||
                   name.Contains("battleye") ||
                   name.Contains("unitycrashhandler") ||
                   name.Contains("pbsvc") ||
                   name.Contains("awesomium") ||
                   name.Contains("createdump") ||
                   name.Contains("crashpad") ||
                   name.Contains("unrealcefsubprocess") ||
                   name.Contains("dxsetup") ||
                   name.Contains("vcredist") ||
                   name.Contains("redist") ||
                   name.Contains("touchup") ||
                   name.Contains("eac_") ||
                   name.Contains("epicwebhelper") ||
                   name.Contains("steamerrorreporter") ||
                   name.Contains("cleanup");
        }
    }
}
