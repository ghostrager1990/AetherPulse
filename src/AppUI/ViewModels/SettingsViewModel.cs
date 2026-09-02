using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AppUI.Services;
using AppUI.Services.Telemetry;

namespace AppUI.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly IAppSettingsService? _settingsService;

        [ObservableProperty]
        private bool _showFloatingHud = true;

        [ObservableProperty]
        private string _hudPosition = "Top Right";

        [ObservableProperty]
        private bool _startWithWindows = false;

        [ObservableProperty]
        private bool _minimizeToTray = false;

        [ObservableProperty]
        private bool _closeToTray = false;

        [ObservableProperty]
        private bool _startMinimized = false;

        [ObservableProperty]
        private bool _enableProxyChaining = true;

        [ObservableProperty]
        private bool _enableHardwareSensorPolling = true;

        public SettingsViewModel()
        {
            LoadFromSettings();
        }

        public SettingsViewModel(IAppSettingsService? settingsService) : this()
        {
            _settingsService = settingsService;
            LoadFromSettings();
        }

        public SettingsViewModel(object? p1) : this(p1 as IAppSettingsService) { }
        public SettingsViewModel(object? p1, object? p2) : this(p1 as IAppSettingsService) { }
        public SettingsViewModel(object? p1, object? p2, object? p3) : this(p1 as IAppSettingsService) { }

        private void LoadFromSettings()
        {
            if (_settingsService?.CurrentSettings != null)
            {
                var s = _settingsService.CurrentSettings;
                StartWithWindows = s.StartWithWindows;
                MinimizeToTray = s.MinimizeToTray;
                CloseToTray = s.CloseToTray;
                StartMinimized = s.StartMinimized;
                EnableProxyChaining = s.EnableProxyChaining;
                EnableHardwareSensorPolling = s.EnableHardwareSensorPolling;

                TelemetryHub.Instance.EnableHardwareSensorPolling = s.EnableHardwareSensorPolling;
            }
        }

        public void LoadValues(object? profile = null) { }

        partial void OnEnableHardwareSensorPollingChanged(bool value)
        {
            TelemetryHub.Instance.EnableHardwareSensorPolling = value;
            if (_settingsService?.CurrentSettings != null)
            {
                _settingsService.CurrentSettings.EnableHardwareSensorPolling = value;
                _settingsService.Save();
            }
        }

        partial void OnShowFloatingHudChanged(bool value)
        {
            if (Application.Current.MainWindow is Views.MainWindow mainWin)
            {
                mainWin.TogglePerformanceOverlay(value);
            }
        }

        [RelayCommand]
        public void SetHudPosition(string position)
        {
            HudPosition = position;
            if (Application.Current.MainWindow is Views.MainWindow mainWin)
            {
                mainWin.SnapHudPosition(position);
            }
        }

        [RelayCommand]
        public void RelaunchAsAdmin()
        {
            var exeName = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exeName))
            {
                var startInfo = new ProcessStartInfo(exeName)
                {
                    UseShellExecute = true,
                    Verb = "runas"
                };
                try
                {
                    Process.Start(startInfo);
                    Application.Current.Shutdown();
                }
                catch
                {
                    // User canceled UAC prompt
                }
            }
        }
    }
}
