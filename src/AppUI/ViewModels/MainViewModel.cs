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
        ArchitectureInfo,
        Settings,
        Help
    }

    public partial class MainViewModel : ObservableObject, IDisposable
    {
        private readonly ITelemetryService _telemetryService;
        private readonly IDeploymentService _deploymentService;
        private readonly IProfileStorageService _profileStorage;
        private readonly IGameSessionManager _sessionManager;
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

        [ObservableProperty]
        private bool _isAlwaysOnTop = false;

        [ObservableProperty]
        private double _overlayOpacity = 1.0;

        public GameProfile GlobalDefaultProfile { get; }

        // Sub-ViewModels
        public LiveDashboardViewModel DashboardVM { get; }
        public QuickStartViewModel QuickStartVM { get; }
        public GameLibraryViewModel LibraryVM { get; }
        public PacingTuningViewModel PacingVM { get; }
        public ArchitectureInfoViewModel ArchitectureVM { get; }
        public SettingsViewModel SettingsVM { get; }
        public HelpViewModel HelpVM { get; }

        public MainViewModel(
            ITelemetryService telemetryService,
            IDeploymentService deploymentService,
            IProfileStorageService profileStorage,
            IGameSessionManager? sessionManager = null,
            IAppSettingsService? appSettings = null)
        {
            _telemetryService = telemetryService;
            _deploymentService = deploymentService;
            _profileStorage = profileStorage;
            _sessionManager = sessionManager ?? new GameSessionManager(_profileStorage, _telemetryService);
            _appSettings = appSettings ?? new AppSettingsService();

            GlobalDefaultProfile = new GameProfile
            {
                Id = "",
                GameName = "Global Default Profile",
                InstallDirectory = "",
                ExecutablePath = ""
            };

            DashboardVM = new LiveDashboardViewModel(_telemetryService, _sessionManager, _profileStorage);
            QuickStartVM = new QuickStartViewModel(this);
            LibraryVM = new GameLibraryViewModel(_deploymentService, _profileStorage, _telemetryService);
            PacingVM = new PacingTuningViewModel();
            ArchitectureVM = new ArchitectureInfoViewModel();
            SettingsVM = new SettingsViewModel(_appSettings);
            HelpVM = new HelpViewModel();

            _activeTuningProfile = GlobalDefaultProfile;
            _currentViewModel = DashboardVM;

            // Wire available tuning profiles
            RebuildAvailableTuningProfiles();
            LibraryVM.GameProfiles.CollectionChanged += (s, e) =>
            {
                OnGameProfilesCollectionChanged(s, e);
                _ = DashboardVM.RefreshTargetProcessListAsync();
            };

            // Wire profile selection and tune game navigation
            LibraryVM.SelectedProfileChanged += OnSelectedProfileChanged;
            LibraryVM.TuneGameRequested += OnTuneGameRequested;

            // Wire root session manager events
            _sessionManager.ActiveProfileChanged += OnSessionActiveProfileChanged;
            _sessionManager.GameLaunched += (profile, proc) =>
            {
                IsAnyGameRunning = true;
                ActiveRunningGameName = profile.GameName;
                ActiveTuningProfile = profile;
            };
            _sessionManager.GameExited += (profile) =>
            {
                IsAnyGameRunning = false;
                ActiveRunningGameName = "None";
            };

            // Initialize tuning sub-viewmodels with active profile
            PacingVM.SetProfile(_activeTuningProfile);

            // Wire telemetry connection status events
            _telemetryService.ConnectionStatusChanged += OnTelemetryConnectionChanged;

            _attachmentTimer = new System.Windows.Threading.DispatcherTimer();
            SetupAttachmentTimer();

            // Start services
            _telemetryService.Start();
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
        }

        private void OnSessionActiveProfileChanged(GameProfile profile)
        {
            if (profile != null)
            {
                ActiveTuningProfile = profile;
            }
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
            var previous = ActiveTuningProfile;

            AvailableTuningProfiles.Clear();
            AvailableTuningProfiles.Add(GlobalDefaultProfile);

            foreach (var profile in LibraryVM.GameProfiles)
            {
                AvailableTuningProfiles.Add(profile);
            }

            var matchingProfile = previous != null 
                ? AvailableTuningProfiles.FirstOrDefault(p => p.Equals(previous)) 
                : null;

            ActiveTuningProfile = matchingProfile ?? GlobalDefaultProfile;
        }

        private readonly System.Windows.Threading.DispatcherTimer _attachmentTimer;

        private void SetupAttachmentTimer()
        {
            _attachmentTimer.Interval = TimeSpan.FromMilliseconds(500);
            _attachmentTimer.Tick += OnAttachmentTimerTick;
            _attachmentTimer.Start();
        }

        private void OnAttachmentTimerTick(object? sender, EventArgs e)
        {
            bool attached = false;
            string attachedName = string.Empty;

            // 1. Check if any library profile process is running
            foreach (var profile in LibraryVM.GameProfiles)
            {
                if (!string.IsNullOrEmpty(profile.ExecutableName))
                {
                    string clean = System.IO.Path.GetFileNameWithoutExtension(profile.ExecutableName);
                    var procs = System.Diagnostics.Process.GetProcessesByName(clean);
                    if (procs.Length > 0)
                    {
                        attached = true;
                        attachedName = profile.GameName;
                        if (ActiveTuningProfile == null || ActiveTuningProfile.Equals(GlobalDefaultProfile) || !ActiveTuningProfile.Equals(profile))
                        {
                            var matchInList = AvailableTuningProfiles.FirstOrDefault(p => p.Equals(profile)) ?? profile;
                            if (!ReferenceEquals(ActiveTuningProfile, matchInList))
                            {
                                ActiveTuningProfile = matchInList;
                            }
                        }
                        break;
                    }
                }
            }

            // 2. Check live dashboard hook status
            if (!attached && DashboardVM.IsHookActive && DashboardVM.FrametimeMs > 0 && DashboardVM.ActiveProcessId > 0)
            {
                attached = true;
                attachedName = $"Direct3D 12 Engine (PID: {DashboardVM.ActiveProcessId})";
            }

            // 3. Check telemetry connection state
            if (!attached && _telemetryService.IsConnected && _telemetryService.LatestTelemetry.CurrentFps > 0)
            {
                attached = true;
                attachedName = _telemetryService.TargetDisplayName != "None (Idle)" 
                    ? _telemetryService.TargetDisplayName 
                    : "Direct3D 12 Game";
            }

            IsAnyGameRunning = attached;
            ActiveRunningGameName = attached ? attachedName : "None";
        }

        public string HeaderStatusText => IsAnyGameRunning 
            ? "ACTIVE (Hook Injected & Telemetry Live)" 
            : "STANDBY (Waiting for Game Injection)";

        public string HeaderStatusColor => IsAnyGameRunning ? "#00FF66" : "#8B949E";

        private void OnTelemetryConnectionChanged(bool isConnected)
        {
            if (isConnected)
            {
                IsAnyGameRunning = true;
                ActiveRunningGameName = !string.IsNullOrWhiteSpace(_telemetryService.LatestTelemetry.ActiveGameTitle) 
                    ? _telemetryService.LatestTelemetry.ActiveGameTitle 
                    : "DirectX 12 Injected";
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
            _telemetryService.ConnectionStatusChanged -= OnTelemetryConnectionChanged;

            _telemetryService.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}

