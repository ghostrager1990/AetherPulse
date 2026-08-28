using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AppUI.Services;

namespace AppUI.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly IAppSettingsService _settingsService;
        private readonly IElevationService _elevationService;

        [ObservableProperty]
        private bool _startWithWindows;

        [ObservableProperty]
        private bool _minimizeToTray;

        [ObservableProperty]
        private bool _closeToTray;

        [ObservableProperty]
        private bool _startMinimized;

        [ObservableProperty]
        private bool _enableProxyChaining;

        [ObservableProperty]
        private bool _isOverlayVisible = true;

        [ObservableProperty]
        private string _selectedOverlayPosition = "Top Left";

        public ObservableCollection<string> AvailablePositions { get; } = new()
        {
            "Top Left",
            "Top Right",
            "Bottom Left",
            "Bottom Right"
        };

        [ObservableProperty]
        private bool _isRunningAsAdmin;

        public string PermissionsDisplayLabel => IsRunningAsAdmin ? "ADMIN" : "STANDARD";
        public string PermissionsColorBrush => IsRunningAsAdmin ? "#3FB950" : "#E3B341";

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        public SettingsViewModel() : this(new AppSettingsService(), new ElevationService())
        {
        }

        public SettingsViewModel(IAppSettingsService? settingsService, IElevationService? elevationService = null)
        {
            _settingsService = settingsService ?? new AppSettingsService();
            _elevationService = elevationService ?? new ElevationService();
            LoadValues();
        }

        public void LoadValues()
        {
            try
            {
                var s = _settingsService?.CurrentSettings;
                if (s != null)
                {
                    StartWithWindows = s.StartWithWindows;
                    MinimizeToTray = s.MinimizeToTray;
                    CloseToTray = s.CloseToTray;
                    StartMinimized = s.StartMinimized;
                    EnableProxyChaining = s.EnableProxyChaining;
                }

                var overlayWin = Application.Current?.Windows?.OfType<Window>()?.FirstOrDefault(w => w.GetType().Name.Contains("Overlay") || w.GetType().Name.Contains("Telemetry"));
                if (overlayWin != null)
                {
                    IsOverlayVisible = overlayWin.Visibility == Visibility.Visible;
                }

                IsRunningAsAdmin = _elevationService?.IsRunningAsAdministrator() ?? false;
                OnPropertyChanged(nameof(PermissionsDisplayLabel));
                OnPropertyChanged(nameof(PermissionsColorBrush));
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to load settings: {ex.Message}";
            }
        }

        partial void OnIsOverlayVisibleChanged(bool value)
        {
            try
            {
                var overlayWin = Application.Current?.Windows?.OfType<Window>()?.FirstOrDefault(w => w.GetType().Name.Contains("Overlay") || w.GetType().Name.Contains("Telemetry"));
                if (overlayWin != null)
                {
                    overlayWin.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            catch { }
        }

        partial void OnSelectedOverlayPositionChanged(string value)
        {
            SetOverlayPosition(value);
        }

        [RelayCommand]
        public void SetOverlayPosition(string position)
        {
            try
            {
                var overlayWin = Application.Current?.Windows?.OfType<Window>()?.FirstOrDefault(w => w.GetType().Name.Contains("Overlay") || w.GetType().Name.Contains("Telemetry"));
                if (overlayWin == null) return;

                double workAreaWidth = SystemParameters.WorkArea.Width;
                double workAreaHeight = SystemParameters.WorkArea.Height;
                double winWidth = overlayWin.ActualWidth > 0 ? overlayWin.ActualWidth : 260;
                double winHeight = overlayWin.ActualHeight > 0 ? overlayWin.ActualHeight : 100;
                const double margin = 20;

                switch (position)
                {
                    case "Top Left":
                        overlayWin.Left = margin;
                        overlayWin.Top = margin;
                        break;
                    case "Top Right":
                        overlayWin.Left = workAreaWidth - winWidth - margin;
                        overlayWin.Top = margin;
                        break;
                    case "Bottom Left":
                        overlayWin.Left = margin;
                        overlayWin.Top = workAreaHeight - winHeight - margin;
                        break;
                    case "Bottom Right":
                        overlayWin.Left = workAreaWidth - winWidth - margin;
                        overlayWin.Top = workAreaHeight - winHeight - margin;
                        break;
                }
            }
            catch { }
        }

        [RelayCommand]
        public void RestartAsAdmin()
        {
            bool success = _elevationService.RestartAsAdministrator();
            if (!success)
            {
                StatusMessage = "Administrator relaunch was cancelled or declined.";
            }
        }

        [RelayCommand]
        public void InstallProxyToGame()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select Game Executable Directory to Install Proxy Shim"
            };

            if (dialog.ShowDialog() == true)
            {
                InstallShimToGameFolder(dialog.FolderName);
            }
        }

        public void InstallShimToGameFolder(string targetDirectory)
        {
            try
            {
                string baseDir = System.AppDomain.CurrentDomain.BaseDirectory;
                string versionDllSource = System.IO.Path.Combine(baseDir, "payload", "version.dll");
                if (!System.IO.File.Exists(versionDllSource))
                {
                    versionDllSource = @"G:\Antigravity Projects\AetherPulse\src\NativeCore\build\Release\version.dll";
                }
                if (System.IO.File.Exists(versionDllSource))
                {
                    System.IO.File.Copy(versionDllSource, System.IO.Path.Combine(targetDirectory, "version.dll"), true);
                }

                string iniPath = System.IO.Path.Combine(targetDirectory, "aetherpulse.ini");
                if (!System.IO.File.Exists(iniPath))
                {
                    System.IO.File.WriteAllText(iniPath, "[Pacing]\nenablePacing=1\ntargetFps=180\n\n[Denoiser]\nenableRayRegen=1\ntemporalWeight=0.85\nvarianceThreshold=0.50\n\n[FSR]\nrcasSharpness=0.35\n");
                }

                StatusMessage = "AetherPulse proxy shim installed successfully!";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Installation error: {ex.Message}";
            }
        }

        partial void OnStartWithWindowsChanged(bool value)
        {
            if (_settingsService?.CurrentSettings != null)
            {
                _settingsService.CurrentSettings.StartWithWindows = value;
                _ = SaveAsync();
            }
        }

        partial void OnMinimizeToTrayChanged(bool value)
        {
            if (_settingsService?.CurrentSettings != null)
            {
                _settingsService.CurrentSettings.MinimizeToTray = value;
                _ = SaveAsync();
            }
        }

        partial void OnCloseToTrayChanged(bool value)
        {
            if (_settingsService?.CurrentSettings != null)
            {
                _settingsService.CurrentSettings.CloseToTray = value;
                _ = SaveAsync();
            }
        }

        partial void OnStartMinimizedChanged(bool value)
        {
            if (_settingsService?.CurrentSettings != null)
            {
                _settingsService.CurrentSettings.StartMinimized = value;
                _ = SaveAsync();
            }
        }

        partial void OnEnableProxyChainingChanged(bool value)
        {
            if (_settingsService?.CurrentSettings != null)
            {
                _settingsService.CurrentSettings.EnableProxyChaining = value;
                _ = SaveAsync();
            }
        }

        private async Task SaveAsync()
        {
            try
            {
                _settingsService?.Save();
                if (_settingsService != null)
                {
                    await _settingsService.SaveSettingsAsync();
                }
                StatusMessage = "Preferences saved.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error saving: {ex.Message}";
            }
        }
    }
}