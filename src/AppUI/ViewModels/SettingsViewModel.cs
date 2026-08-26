using System.Threading.Tasks;
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
        private bool _isRunningAsAdmin;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        public SettingsViewModel(IAppSettingsService settingsService, IElevationService? elevationService = null)
        {
            _settingsService = settingsService;
            _elevationService = elevationService ?? new ElevationService();
            LoadValues();
        }

        public void LoadValues()
        {
            var s = _settingsService.CurrentSettings;
            StartWithWindows = s.StartWithWindows;
            MinimizeToTray = s.MinimizeToTray;
            CloseToTray = s.CloseToTray;
            StartMinimized = s.StartMinimized;
            EnableProxyChaining = s.EnableProxyChaining;
            IsRunningAsAdmin = _elevationService.IsRunningAsAdministrator();
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

        partial void OnStartWithWindowsChanged(bool value)
        {
            _settingsService.CurrentSettings.StartWithWindows = value;
            _ = SaveAsync();
        }

        partial void OnMinimizeToTrayChanged(bool value)
        {
            _settingsService.CurrentSettings.MinimizeToTray = value;
            _ = SaveAsync();
        }

        partial void OnCloseToTrayChanged(bool value)
        {
            _settingsService.CurrentSettings.CloseToTray = value;
            _ = SaveAsync();
        }

        partial void OnStartMinimizedChanged(bool value)
        {
            _settingsService.CurrentSettings.StartMinimized = value;
            _ = SaveAsync();
        }

        partial void OnEnableProxyChainingChanged(bool value)
        {
            _settingsService.CurrentSettings.EnableProxyChaining = value;
            _ = SaveAsync();
        }

        private async Task SaveAsync()
        {
            _settingsService.Save();
            await _settingsService.SaveSettingsAsync();
            StatusMessage = "Preferences saved.";
        }
    }
}
