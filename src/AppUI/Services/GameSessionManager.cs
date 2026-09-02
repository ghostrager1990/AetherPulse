using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using AppUI.Models;
using AppUI.Services.Telemetry;

namespace AppUI.Services
{
    public interface IGameSessionManager
    {
        GameProfile? ActiveGame { get; }
        Process? ActiveProcess { get; }
        bool IsGameRunning { get; }

        event Action<GameProfile, Process>? GameLaunched;
        event Action<GameProfile>? GameExited;
        event Action<GameProfile>? ActiveProfileChanged;

        void SetActiveProfile(GameProfile profile);
        void StartMonitoring();
        void StopMonitoring();
    }

    public partial class GameSessionManager : ObservableObject, IGameSessionManager, IDisposable
    {
        public static GameSessionManager? Instance { get; private set; }

        private readonly IProfileStorageService _storageService;
        private readonly ITelemetryService _telemetryService;

        [ObservableProperty]
        private GameProfile? _activeGame;

        [ObservableProperty]
        private Process? _activeProcess;

        [ObservableProperty]
        private bool _isGameRunning;

        public event Action<GameProfile, Process>? GameLaunched;
        public event Action<GameProfile>? GameExited;
        public event Action<GameProfile>? ActiveProfileChanged;

        private CancellationTokenSource? _cts;
        private Task? _monitorTask;
        private int _lastActivePid = 0;

        public GameSessionManager(IProfileStorageService storageService, ITelemetryService telemetryService)
        {
            Instance = this;
            _storageService = storageService;
            _telemetryService = telemetryService;
            StartMonitoring();
        }

        public void SetActiveProfile(GameProfile profile)
        {
            ActiveGame = profile;
            if (!string.IsNullOrWhiteSpace(profile?.ExecutablePath))
            {
                _telemetryService.SetActiveTarget(Path.GetFileName(profile.ExecutablePath), profile.GameName);
            }
            ActiveProfileChanged?.Invoke(profile!);
        }

        public void StartMonitoring()
        {
            if (_monitorTask != null && !_monitorTask.IsCompleted) return;

            _cts = new CancellationTokenSource();
            _monitorTask = Task.Run(() => MonitorLoopAsync(_cts.Token));
        }

        public void StopMonitoring()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _monitorTask = null;
        }

        private async Task MonitorLoopAsync(CancellationToken token)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));

            while (!token.IsCancellationRequested && await timer.WaitForNextTickAsync(token))
            {
                try
                {
                    var profiles = await _storageService.LoadProfilesAsync();
                    Process? detectedProc = null;
                    GameProfile? detectedProfile = null;

                    // 1. Check existing active process
                    if (ActiveProcess != null)
                    {
                        try
                        {
                            if (!ActiveProcess.HasExited)
                            {
                                detectedProc = ActiveProcess;
                                detectedProfile = ActiveGame;
                            }
                        }
                        catch
                        {
                            detectedProc = null;
                        }
                    }

                    // 2. Scan running processes by executable name
                    if (detectedProc == null)
                    {
                        var runningProcesses = Process.GetProcesses();
                        foreach (var proc in runningProcesses)
                        {
                            try
                            {
                                if (proc.Id <= 4) continue;
                                string procName = proc.ProcessName;

                                foreach (var profile in profiles)
                                {
                                    if (string.IsNullOrWhiteSpace(profile.ExecutablePath)) continue;

                                    string cleanExe = Path.GetFileNameWithoutExtension(profile.ExecutablePath);
                                    if (procName.Equals(cleanExe, StringComparison.OrdinalIgnoreCase))
                                    {
                                        detectedProc = proc;
                                        detectedProfile = profile;
                                        break;
                                    }
                                }

                                if (detectedProc != null) break;
                            }
                            catch { }
                            finally
                            {
                                if (proc != detectedProc) proc.Dispose();
                            }
                        }
                    }

                    // 3. Handle state transitions
                    if (detectedProc != null && detectedProfile != null)
                    {
                        if (!IsGameRunning || _lastActivePid != detectedProc.Id)
                        {
                            _lastActivePid = detectedProc.Id;
                            ActiveProcess = detectedProc;
                            ActiveGame = detectedProfile;
                            IsGameRunning = true;
                            detectedProfile.IsGameRunning = true;

                            string targetExe = Path.GetFileName(detectedProfile.ExecutablePath);
                            _telemetryService.SetActiveTarget(targetExe, detectedProfile.Name);
                            TelemetryHub.Instance.WakeUp(detectedProc.Id, detectedProc.ProcessName);
                            
                            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                GameLaunched?.Invoke(detectedProfile, detectedProc);
                                ActiveProfileChanged?.Invoke(detectedProfile);
                            }));
                        }
                    }
                    else if (IsGameRunning)
                    {
                        var exitedProfile = ActiveGame;
                        _lastActivePid = 0;
                        ActiveProcess = null;
                        IsGameRunning = false;

                        if (exitedProfile != null)
                        {
                            exitedProfile.IsGameRunning = false;
                        }

                        TelemetryHub.Instance.Sleep();

                        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (exitedProfile != null)
                            {
                                GameExited?.Invoke(exitedProfile);
                            }
                        }));
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch { }
            }
        }

        public void Dispose()
        {
            StopMonitoring();
        }
    }
}
