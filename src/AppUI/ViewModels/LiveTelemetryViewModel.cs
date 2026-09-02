using System;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using AppUI.Models;
using AppUI.Services;

namespace AppUI.ViewModels
{
    public partial class LiveTelemetryViewModel : ObservableObject
    {
        private readonly ITelemetryService _telemetryService;
        private readonly IGameSessionManager? _sessionManager;

        [ObservableProperty]
        private float _currentFps = 0;

        [ObservableProperty]
        private float _fps = 0;

        [ObservableProperty]
        private float _averageFps = 0;

        [ObservableProperty]
        private float _frametimeMs = 0;

        [ObservableProperty]
        private float _frameTime = 0;

        [ObservableProperty]
        private float _onePercentLow = 0;

        [ObservableProperty]
        private float _pacingJitter = 0;

        [ObservableProperty]
        private float _jitter = 0;

        [ObservableProperty]
        private uint _totalFrames = 0;

        [ObservableProperty]
        private uint _droppedFrames = 0;

        [ObservableProperty]
        private bool _isHookActive = false;

        [ObservableProperty]
        private bool _isStandby = true;

        [ObservableProperty]
        private bool _isRayRegenActive = false;

        [ObservableProperty]
        private string _gameTitle = string.Empty;

        [ObservableProperty]
        private string _connectionStatus = "STANDBY (Waiting for Game Injection)";

        public LiveTelemetryViewModel(ITelemetryService telemetryService, IGameSessionManager? sessionManager = null)
        {
            _telemetryService = telemetryService;
            _sessionManager = sessionManager;

            _telemetryService.TelemetryUpdated += OnTelemetryUpdated;
            _telemetryService.ConnectionStatusChanged += OnConnectionStatusChanged;

            if (_sessionManager != null)
            {
                _sessionManager.GameLaunched += OnGameLaunched;
                _sessionManager.GameExited += OnGameExited;
            }

            _telemetryService.Start();
        }

        public LiveTelemetryViewModel() : this(new TelemetryService()) { }

        private void OnGameLaunched(GameProfile profile, Process process)
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                IsHookActive = true;
                IsStandby = false;
                GameTitle = profile.GameName;
                ConnectionStatus = $"ACTIVE ({profile.GameName} - Hooked)";
            }));
        }

        private void OnGameExited(GameProfile profile)
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                IsHookActive = false;
                IsStandby = true;
                CurrentFps = 0;
                Fps = 0;
                FrametimeMs = 0;
                FrameTime = 0;
                ConnectionStatus = "STANDBY (Waiting for Game Injection)";
            }));
        }

        private void OnConnectionStatusChanged(bool isConnected)
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                IsHookActive = isConnected;
                IsStandby = !isConnected;
                ConnectionStatus = isConnected 
                    ? "ACTIVE (In-Game Cadence Hooked)" 
                    : "STANDBY (Waiting for Game Injection)";
            }));
        }

        private void OnTelemetryUpdated(AetherTelemetryData data)
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                float activeFps = data.CurrentFps > 0 ? data.CurrentFps : data.AverageFps;
                CurrentFps = (float)Math.Round(activeFps, 1);
                Fps = CurrentFps;
                AverageFps = (float)Math.Round(data.AverageFps, 1);
                FrametimeMs = (float)Math.Round(data.FrameTimeMs, 2);
                FrameTime = FrametimeMs;
                OnePercentLow = (float)Math.Round(data.AverageFps, 1);
                PacingJitter = (float)Math.Round(data.PacingJitterMs, 2);
                Jitter = PacingJitter;
                TotalFrames = data.FrameIndex;
                DroppedFrames = data.DroppedFrames;
                IsHookActive = data.PacerActive || data.FrameIndex > 0 || (_sessionManager?.IsGameRunning ?? false);
                IsStandby = !IsHookActive;
                IsRayRegenActive = data.RayRegenActive;
                if (!string.IsNullOrWhiteSpace(data.ActiveGameTitle))
                {
                    GameTitle = data.ActiveGameTitle;
                }
            }));
        }
    }
}
