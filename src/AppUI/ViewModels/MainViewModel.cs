using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AppUI.Models;
using AppUI.Services;

namespace AppUI.ViewModels
{
    public enum NavigationPage
    {
        Dashboard,
        QuickStart,
        Library,
        PacingTuning,
        FsrTuning,
        RayRegenTuning,
        ArchitectureInfo,
        Settings,
        Help
    }

    public partial class MainViewModel : ObservableObject, IDisposable
    {
        private readonly ITelemetryService _telemetryService;
        private readonly IProcessWatcherService _processWatcher;
        private readonly IDeploymentService _deploymentService;
        private readonly IProfileStorageService _profileStorage;
        private readonly IAppSettingsService _appSettings;

        [ObservableProperty]
        private NavigationPage _currentPage = NavigationPage.Dashboard;

        [ObservableProperty]
        private ObservableObject _currentViewModel;

        [ObservableProperty]
        private string _activeRunningGameName = "None";

        [ObservableProperty]
        private bool _isAnyGameRunning;

        [ObservableProperty]
        private ObservableCollection<GameProfile> _availableTuningProfiles = new();

        [ObservableProperty]
        private GameProfile _activeTuningProfile;

        public GameProfile GlobalDefaultProfile { get; }

        // Sub-ViewModels
        public LiveDashboardViewModel DashboardVM { get; }
        public QuickStartViewModel QuickStartVM { get; }
        public GameLibraryViewModel LibraryVM { get; }
        public PacingTuningViewModel PacingVM { get; }
        public FSRTuningViewModel FsrVM { get; }
        public RayRegenTuningViewModel RayRegenVM { get; }
        public ArchitectureInfoViewModel ArchitectureVM { get; }
        public SettingsViewModel SettingsVM { get; }
        public HelpViewModel HelpVM { get; }

        public MainViewModel(
            ITelemetryService telemetryService,
            IProcessWatcherService processWatcher,
            IDeploymentService deploymentService,
            IProfileStorageService profileStorage,
            IAppSettingsService? appSettings = null)
        {
            _telemetryService = telemetryService;
            _processWatcher = processWatcher;
            _deploymentService = deploymentService;
            _profileStorage = profileStorage;
            _appSettings = appSettings ?? new AppSettingsService();

            GlobalDefaultProfile = new GameProfile
            {
                Id = "",
                GameName = "Global Default Profile",
                InstallDirectory = "",
                ExecutablePath = ""
            };

            DashboardVM = new LiveDashboardViewModel(_telemetryService);
            QuickStartVM = new QuickStartViewModel(this);
            LibraryVM = new GameLibraryViewModel(_deploymentService, _profileStorage, _processWatcher);
            PacingVM = new PacingTuningViewModel();
            FsrVM = new FSRTuningViewModel();
            RayRegenVM = new RayRegenTuningViewModel();
            ArchitectureVM = new ArchitectureInfoViewModel();
            SettingsVM = new SettingsViewModel(_appSettings);
            HelpVM = new HelpViewModel();

            _activeTuningProfile = GlobalDefaultProfile;
            _currentViewModel = DashboardVM;

            // Wire available tuning profiles
            RebuildAvailableTuningProfiles();
            LibraryVM.GameProfiles.CollectionChanged += OnGameProfilesCollectionChanged;

            // Wire profile selection and tune game navigation
            LibraryVM.SelectedProfileChanged += OnSelectedProfileChanged;
            LibraryVM.TuneGameRequested += OnTuneGameRequested;

            // Initialize tuning sub-viewmodels with active profile
            PacingVM.SetProfile(_activeTuningProfile);
            FsrVM.SetProfile(_activeTuningProfile);
            RayRegenVM.SetProfile(_activeTuningProfile);

            // Wire process watcher events
            _processWatcher.OnGameStarted += OnProcessStarted;
            _processWatcher.OnGameStopped += OnProcessStopped;

            // Start services
            _telemetryService.Start();
            _processWatcher.Start();
            _ = InitializeProfilesAndSettingsAsync();
        }

        private async System.Threading.Tasks.Task InitializeProfilesAndSettingsAsync()
        {
            await _appSettings.LoadSettingsAsync();
            SettingsVM.LoadValues();
            await LibraryVM.LoadProfilesAsync();
            RebuildAvailableTuningProfiles();
        }

        partial void OnActiveTuningProfileChanged(GameProfile value)
        {
            PacingVM.SetProfile(value);
            FsrVM.SetProfile(value);
            RayRegenVM.SetProfile(value);
        }

        private void OnTuneGameRequested(object? sender, GameProfile profile)
        {
            ActiveTuningProfile = profile;
            Navigate(NavigationPage.PacingTuning);
        }

        private void OnSelectedProfileChanged(object? sender, GameProfile? profile)
        {
            if (profile != null)
            {
                ActiveTuningProfile = profile;
            }
        }

        private void OnGameProfilesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RebuildAvailableTuningProfiles();
        }

        private void RebuildAvailableTuningProfiles()
        {
            AvailableTuningProfiles.Clear();
            AvailableTuningProfiles.Add(GlobalDefaultProfile);

            foreach (var profile in LibraryVM.GameProfiles)
            {
                AvailableTuningProfiles.Add(profile);
            }

            if (ActiveTuningProfile == null || !AvailableTuningProfiles.Contains(ActiveTuningProfile))
            {
                ActiveTuningProfile = GlobalDefaultProfile;
            }
        }

        private void OnProcessStarted(object? sender, GameProcessInfo info)
        {
            IsAnyGameRunning = true;
            ActiveRunningGameName = info.ProcessName;

            var matching = LibraryVM.GameProfiles.FirstOrDefault(p =>
                string.Equals(p.ExecutableName, info.ProcessName + ".exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.GameName, info.ProcessName, StringComparison.OrdinalIgnoreCase));

            if (matching != null)
            {
                matching.IsGameRunning = true;
            }
        }

        private void OnProcessStopped(object? sender, GameProcessInfo info)
        {
            var matching = LibraryVM.GameProfiles.FirstOrDefault(p =>
                string.Equals(p.ExecutableName, info.ProcessName + ".exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.GameName, info.ProcessName, StringComparison.OrdinalIgnoreCase));

            if (matching != null)
            {
                matching.IsGameRunning = false;
            }

            if (!_processWatcher.ActiveWatchedProcesses.Any())
            {
                IsAnyGameRunning = false;
                ActiveRunningGameName = "None";
            }
        }

        [RelayCommand]
        public void Navigate(NavigationPage page)
        {
            CurrentPage = page;
            CurrentViewModel = page switch
            {
                NavigationPage.Dashboard => DashboardVM,
                NavigationPage.QuickStart => QuickStartVM,
                NavigationPage.Library => LibraryVM,
                NavigationPage.PacingTuning => PacingVM,
                NavigationPage.FsrTuning => FsrVM,
                NavigationPage.RayRegenTuning => RayRegenVM,
                NavigationPage.ArchitectureInfo => ArchitectureVM,
                NavigationPage.Settings => SettingsVM,
                NavigationPage.Help => HelpVM,
                _ => DashboardVM
            };
        }

        public void Dispose()
        {
            LibraryVM.GameProfiles.CollectionChanged -= OnGameProfilesCollectionChanged;
            LibraryVM.SelectedProfileChanged -= OnSelectedProfileChanged;
            LibraryVM.TuneGameRequested -= OnTuneGameRequested;
            _processWatcher.OnGameStarted -= OnProcessStarted;
            _processWatcher.OnGameStopped -= OnProcessStopped;

            DashboardVM.Dispose();
            _processWatcher.Dispose();
            _telemetryService.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
