using System;
using System.IO;
using System.Text;
using AppUI.Models;
using AppUI.Services.Telemetry;

namespace AppUI.Services
{
    public class TelemetryService : ITelemetryService, IDisposable
    {
        private static TelemetryService? _instance;
        public static TelemetryService Instance => _instance ??= new TelemetryService();

        public event Action<AetherTelemetryData>? TelemetryUpdated;
        public event Action<bool>? ConnectionStatusChanged;

        public AetherTelemetryData LatestTelemetry { get; private set; } = AetherTelemetryData.Empty;
        public bool IsConnected => TelemetryHub.Instance.CurrentSnapshot.IsLive;
        public bool IsGameAttached => IsConnected;
        public bool IsTargetActive => IsConnected;

        public int PollingIntervalMs { get; set; } = 50;
        public string TargetProcessName { get; set; } = string.Empty;
        public string TargetDisplayName { get; set; } = string.Empty;

        private string _activeTargetExe = string.Empty;
        public string ActiveTargetExe
        {
            get => _activeTargetExe;
            set
            {
                if (_activeTargetExe != value)
                {
                    _activeTargetExe = value;
                    TargetProcessName = value;
                    TelemetryHub.Instance.LockTarget(value);
                }
            }
        }

        public TelemetryService()
        {
            _instance = this;
            TelemetryHub.Instance.OnTelemetryUpdated += HandleTelemetrySnapshot;
        }

        private void HandleTelemetrySnapshot(TelemetrySnapshot snap)
        {
            byte[] titleBytes = new byte[128];
            string proc = Path.GetFileNameWithoutExtension(ActiveTargetExe);
            if (!string.IsNullOrEmpty(proc))
            {
                Encoding.ASCII.GetBytes(proc, 0, Math.Min(proc.Length, 127), titleBytes, 0);
            }

            var data = new AetherTelemetryData
            {
                Sequence = 1,
                StructVersion = 0xAEE2,
                FrameIndex = 1,
                CurrentFps = (float)snap.Fps,
                AverageFps = (float)snap.Low1PercentFps,
                FrameTimeMs = (float)snap.FrametimeMs,
                PacingJitterMs = (float)snap.PacingJitterPct,
                DroppedFrames = 0,
                IsPacerActive = (byte)(snap.IsLive ? 1 : 0),
                IsRayRegenActive = 0,
                ActiveDenoiserFlags = 0,
                CadenceRatio = (float)snap.CadenceRatio,
                SubFrameVarianceUs = (float)snap.SubFrameVarianceUs,
                RealTimeDeltaMs = (float)snap.RealTimeDeltaMs,
                IsExternalLimiterActive = (byte)(snap.IsExternalLimiterActive ? 1 : 0),
                RawGameTitle = titleBytes
            };

            LatestTelemetry = data;
            ConnectionStatusChanged?.Invoke(snap.IsLive);
            TelemetryUpdated?.Invoke(data);
        }

        public void Start()
        {
            TelemetryHub.Instance.LockTarget(ActiveTargetExe);
        }

        public void Stop()
        {
            TelemetryHub.Instance.LockTarget(string.Empty);
            ConnectionStatusChanged?.Invoke(false);
        }

        public void Reset()
        {
            LatestTelemetry = AetherTelemetryData.Empty;
            ConnectionStatusChanged?.Invoke(false);
        }

        public void SetActiveTarget(string gameTitle, string executablePath)
        {
            TargetDisplayName = gameTitle;
            ActiveTargetExe = Path.GetFileName(executablePath);
        }

        public void ForceAttachToProcess(string processName = "")
        {
            ActiveTargetExe = processName;
        }

        public void SetTargetFramerateLimit(uint targetFps)
        {
            // Clean no-op in isolated TelemetryHub architecture
        }

        public void Dispose()
        {
            TelemetryHub.Instance.OnTelemetryUpdated -= HandleTelemetrySnapshot;
        }
    }
}

