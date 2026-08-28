using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AppUI.Models;

namespace AppUI.Services
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct AetherPulseSharedState
    {
        public uint ProcessId;
        public ulong FrameIndex;
        public double InstantFps;
        public double AverageFps;
        public double FrameTimeMs;
        public double PacingJitterMs;
        public uint MissedDeadlines;
        [MarshalAs(UnmanagedType.I1)]
        public bool DxgiPacerActive;
        [MarshalAs(UnmanagedType.I1)]
        public bool RayRegenActive;
        public uint InterceptedRadianceWidth;
        public uint InterceptedRadianceHeight;
    }

    public class TelemetryService : ITelemetryService
    {
        public const string SharedStateMapName = @"Global\AetherPulse_SharedState_v1";

        private readonly object _lock = new();
        private CancellationTokenSource? _cts;
        private Task? _pollingTask;
        private MemoryMappedFile? _mmf;
        private MemoryMappedViewAccessor? _accessor;

        private AetherTelemetryData _latestTelemetry = AetherTelemetryData.Empty;
        private bool _isConnected;
        private int _pollingIntervalMs = 16; // ~60 Hz

        public event EventHandler<AetherTelemetryData>? TelemetryUpdated;
        public event EventHandler<bool>? ConnectionStatusChanged;
        public event Action<AetherPulseSharedState>? OnTelemetryUpdated;

        private string _activeGameTitle = string.Empty;
        private string _activeExeName = string.Empty;

        public string TargetProcessName
        {
            get => _activeExeName;
            set
            {
                _activeExeName = Path.GetFileNameWithoutExtension(value ?? string.Empty);
                TargetDisplayName = string.IsNullOrEmpty(_activeExeName) ? "None (Idle)" : _activeExeName;
                IsTargetActive = !string.IsNullOrEmpty(_activeExeName);
            }
        }

        public string TargetDisplayName { get; private set; } = "None (Idle)";
        public bool IsTargetActive { get; private set; }
        public bool IsGameAttached => _isConnected && _latestTelemetry.CurrentFps > 0;

        public void SetActiveTarget(string gameTitle, string executablePath)
        {
            _activeGameTitle = gameTitle;
            _activeExeName = Path.GetFileNameWithoutExtension(executablePath);
            TargetDisplayName = $"{gameTitle} ({Path.GetFileName(executablePath)})";
            IsTargetActive = true;
        }

        public void ForceAttachToProcess(string processName = "CrimsonDesert")
        {
            TargetProcessName = processName;
            TargetDisplayName = $"{processName}.exe";
            IsTargetActive = true;
            TryOpenMemoryMap();
            Start();
        }

        public AetherTelemetryData LatestTelemetry
        {
            get
            {
                lock (_lock) return _latestTelemetry;
            }
            private set
            {
                lock (_lock) _latestTelemetry = value;
            }
        }

        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (_isConnected != value)
                {
                    _isConnected = value;
                    ConnectionStatusChanged?.Invoke(this, value);
                }
            }
        }

        public int PollingIntervalMs
        {
            get => _pollingIntervalMs;
            set => _pollingIntervalMs = Math.Clamp(value, 1, 1000);
        }

        public void Start()
        {
            lock (_lock)
            {
                if (_pollingTask != null && !_pollingTask.IsCompleted) return;

                _cts = new CancellationTokenSource();
                _pollingTask = Task.Run(() => PollingLoopAsync(_cts.Token));
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;
                _pollingTask = null;
                CloseMemoryMap();
                IsConnected = false;
            }
        }

        private async Task PollingLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                if (_accessor == null)
                {
                    TryOpenMemoryMap();
                }

                if (_accessor != null)
                {
                    try
                    {
                        _accessor.Read(0, out AetherPulseSharedState state);

                        if (state.ProcessId > 0 && state.FrameIndex > 0)
                        {
                            IsTargetActive = true;
                            if (string.IsNullOrEmpty(TargetDisplayName) || TargetDisplayName == "None (Idle)")
                            {
                                TargetDisplayName = !string.IsNullOrEmpty(_activeGameTitle)
                                    ? $"{_activeGameTitle} ({_activeExeName}.exe)"
                                    : "Direct3D 12 Title (Active)";
                            }

                            var tele = new AetherTelemetryData
                            {
                                StructVersion = 1,
                                FrameIndex = (uint)state.FrameIndex,
                                CurrentFps = (float)state.InstantFps,
                                AverageFps = (float)state.AverageFps,
                                FrameTimeMs = (float)state.FrameTimeMs,
                                PacingJitterMs = (float)state.PacingJitterMs,
                                IsPacerActive = state.DxgiPacerActive,
                                IsRayRegenActive = state.RayRegenActive,
                                DroppedFrames = state.MissedDeadlines,
                                ActiveGameTitle = !string.IsNullOrEmpty(_activeGameTitle) ? _activeGameTitle : "Crimson Desert"
                            };

                            LatestTelemetry = tele;
                            IsConnected = true;

                            OnTelemetryUpdated?.Invoke(state);

                            if (System.Windows.Application.Current?.Dispatcher != null)
                            {
                                _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                                {
                                    TelemetryUpdated?.Invoke(this, tele);
                                });
                            }
                            else
                            {
                                TelemetryUpdated?.Invoke(this, tele);
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // Game process may have exited; clean up cleanly
                        CloseMemoryMap();
                        IsConnected = false;
                    }
                }

                try
                {
                    await Task.Delay(_pollingIntervalMs, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private void TryOpenMemoryMap()
        {
            try
            {
                _mmf = MemoryMappedFile.OpenExisting(SharedStateMapName, MemoryMappedFileRights.Read);
            }
            catch (FileNotFoundException)
            {
                try
                {
                    _mmf = MemoryMappedFile.OpenExisting(@"Local\AetherPulse_SharedState_v1", MemoryMappedFileRights.Read);
                }
                catch (FileNotFoundException)
                {
                    _mmf = null;
                }
            }
            catch (Exception)
            {
                _mmf = null;
            }

            if (_mmf != null)
            {
                try
                {
                    _accessor = _mmf.CreateViewAccessor(0, Marshal.SizeOf<AetherPulseSharedState>(), MemoryMappedFileAccess.Read);
                }
                catch (Exception)
                {
                    CloseMemoryMap();
                }
            }
        }

        private void CloseMemoryMap()
        {
            try { _accessor?.Dispose(); } catch { }
            _accessor = null;

            try { _mmf?.Dispose(); } catch { }
            _mmf = null;
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }
    }
}