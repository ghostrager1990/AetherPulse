using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
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
        private readonly IProcessWatcherService _processWatcher;

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
            IProcessWatcherService processWatcher)
        {
            _deploymentService = deploymentService;
            _storageService = storageService;
            _processWatcher = processWatcher;
        }

        partial void OnSelectedGameProfileChanged(GameProfile? value)
        {
            SelectedProfileChanged?.Invoke(this, value);
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
                // Purge blacklisted helper / crash reporter executables from saved library
                if (IsExcludedExecutable(profile.ExecutablePath))
                {
                    purgedAny = true;
                    continue;
                }

                // Refresh deployed status
                if (!string.IsNullOrEmpty(profile.InstallDirectory))
                {
                    profile.IsHookDeployed = _deploymentService.IsDeployed(profile.InstallDirectory, profile.Mode);
                    _processWatcher.RegisterTarget(profile.ExecutableName);
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

        // Anti-Cheat Hazard Warning Modal State
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

            // Run pre-import compatibility verification
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

            // Try extracting product name metadata
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

            // Also un-ignore if previously ignored
            var ignoredPaths = await _storageService.LoadIgnoredGamesAsync();
            if (ignoredPaths.Remove(executablePath))
            {
                await _storageService.SaveIgnoredGamesAsync(ignoredPaths);
            }

            // Scan for Anti-Cheat protection
            var acScan = AntiCheatDetectionService.ScanGame(executablePath, installDir);

            var profile = new GameProfile
            {
                GameName = gameName,
                ExecutablePath = executablePath,
                InstallDirectory = installDir,
                Mode = DeploymentMode.DxgiProxy,
                IsHookDeployed = _deploymentService.IsDeployed(installDir, DeploymentMode.DxgiProxy),
                HasAntiCheatWarning = acScan.IsOnlineOrProtectedGame,
                DetectedAntiCheatName = acScan.DetectedSystem
            };

            GameProfiles.Add(profile);
            SelectedGameProfile = profile;
            _processWatcher.RegisterTarget(profile.ExecutableName);

            await SaveProfilesAsync();
            StatusMessage = $"Added '{gameName}' to library.";
        }

        [RelayCommand]
        public async Task AutoScanGamesAsync()
        {
            IsScanning = true;
            StatusMessage = "Scanning DirectX 12 & Vulkan libraries for games...";

            int foundCount = 0;
            var ignoredCandidates = new List<GameProfile>();

            try
            {
                var ignoredPaths = await _storageService.LoadIgnoredGamesAsync();

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
                                    var exeFiles = Directory.GetFiles(dir, "*.exe", SearchOption.TopDirectoryOnly)
                                        .Where(f => !IsExcludedExecutable(f))
                                        .ToList();

                                    foreach (var exe in exeFiles)
                                    {
                                        try
                                        {
                                            string fullExePath = Path.GetFullPath(exe);
                                            if (GameProfiles.Any(p => string.Equals(p.ExecutablePath, fullExePath, StringComparison.OrdinalIgnoreCase)))
                                            {
                                                continue;
                                            }

                                            // Strictly filter for 64-bit DirectX 12 / Vulkan modern games
                                            if (!IsDirectX12OrVulkanExecutable(fullExePath, dir))
                                            {
                                                continue;
                                            }

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
                                                Mode = DeploymentMode.DxgiProxy,
                                                IsHookDeployed = _deploymentService.IsDeployed(dir, DeploymentMode.DxgiProxy)
                                            };

                                            // Check if previously ignored
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
                                                    _processWatcher.RegisterTarget(profile.ExecutableName);
                                                });
                                            }
                                            else
                                            {
                                                GameProfiles.Add(profile);
                                                _processWatcher.RegisterTarget(profile.ExecutableName);
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

                // If any previously ignored games were found, open the Restore dialog
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
                                _processWatcher.RegisterTarget(p.ExecutableName);
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
            if (target == null || string.IsNullOrWhiteSpace(target.InstallDirectory))
            {
                StatusMessage = "No game selected for deployment.";
                return;
            }

            // Anti-Cheat Hazard Check
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
            // Write custom INI to temp path
            string tempIniPath = Path.Combine(Path.GetTempPath(), $"aetherpulse_{target.Id}.ini");
            await File.WriteAllTextAsync(tempIniPath, target.GenerateIniContent());

            var result = await _deploymentService.DeployAsync(target.InstallDirectory, target.Mode, tempIniPath);

            try { File.Delete(tempIniPath); } catch { }

            if (result.Succeeded)
            {
                target.IsHookDeployed = true;
                target.LastDeployedAt = DateTime.Now;
                StatusMessage = $"Deployed AetherPulse ({target.Mode}) for {target.GameName}.";
                await SaveProfilesAsync();
            }
            else
            {
                StatusMessage = $"Deployment error: {result.Message}";
            }
        }

        [RelayCommand]
        public async Task RemoveHookAsync(GameProfile? profile)
        {
            var target = profile ?? SelectedGameProfile;
            if (target == null || string.IsNullOrWhiteSpace(target.InstallDirectory))
            {
                StatusMessage = "No game selected.";
                return;
            }

            var result = await _deploymentService.UninstallAsync(target.InstallDirectory, target.Mode);
            if (result.Succeeded)
            {
                target.IsHookDeployed = false;
                StatusMessage = $"Uninstalled hook files from {target.GameName}.";
                await SaveProfilesAsync();
            }
            else
            {
                StatusMessage = $"Uninstall error: {result.Message}";
            }
        }

        [RelayCommand]
        public async Task DeleteGameFromLibraryAsync(GameProfile? profile)
        {
            var target = profile ?? SelectedGameProfile;
            if (target == null) return;

            // Unregister process watcher
            _processWatcher.UnregisterTarget(target.ExecutableName);

            // Add to ignored games
            var ignored = await _storageService.LoadIgnoredGamesAsync();
            ignored.Add(target.ExecutablePath);
            await _storageService.SaveIgnoredGamesAsync(ignored);

            // Remove from active list
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

            // Anti-Cheat Safety Warning on Launch
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
                var startInfo = new ProcessStartInfo
                {
                    FileName = target.ExecutablePath,
                    WorkingDirectory = target.InstallDirectory,
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

                // 1. Check Directory and Subdirectories for DirectX 12 / Vulkan / Streamline files
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

                // 2. Inspect PE Header (x64 check and PE Import Table byte scanning)
                using var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (fs.Length < 0x40) return false;

                using var reader = new BinaryReader(fs);
                ushort dosMagic = reader.ReadUInt16();
                if (dosMagic != 0x5A4D) return false; // "MZ"

                // Read DOS e_lfanew
                fs.Seek(0x3C, SeekOrigin.Begin);
                int peOffset = reader.ReadInt32();

                if (peOffset <= 0 || peOffset + 0x18 > fs.Length) return false;

                fs.Seek(peOffset, SeekOrigin.Begin);
                uint peSignature = reader.ReadUInt32();
                if (peSignature != 0x00004550) return false; // "PE\0\0"

                ushort machine = reader.ReadUInt16();
                if (machine != 0x8664) // Must be 64-bit AMD64 binary
                {
                    return false;
                }

                // Scan first 1MB of binary for d3d12.dll, vulkan-1.dll, dxgi.dll imports
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

            // Ignore subfolders named Installer, Redist, Support, DirectX, DotNet
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
