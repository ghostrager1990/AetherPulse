using System;
using System.Threading;
using System.Threading.Tasks;

namespace AppUI.Services.Telemetry
{
    public record TelemetrySnapshot(
        double Fps,
        double FrametimeMs,
        double Low1PercentFps,
        double PacingJitterPct,
        double GpuActivityPercent,
        string ActiveEngine, // "PresentMon Core" or "STANDBY"
        bool IsLive,
        double CadenceRatio = 0.50,
        double SubFrameVarianceUs = 0.0,
        double RealTimeDeltaMs = 0.0,
        bool IsExternalLimiterActive = false
    )
    {
        // 6-parameter compatibility constructor
        public TelemetrySnapshot(double fps, double frametimeMs, double low1PercentFps, double pacingJitterPct, string activeEngine, bool isLive)
            : this(fps, frametimeMs, low1PercentFps, pacingJitterPct, 0.0, activeEngine, isLive, 0.50, 0.0, frametimeMs, false) { }
    }

    public sealed class TelemetryHub : IDisposable
    {
        public static TelemetryHub Instance { get; } = new();

        public TelemetrySnapshot CurrentSnapshot { get; private set; } = new(0, 0, 0, 0, 0.0, "STANDBY", false);
        public event Action<TelemetrySnapshot>? OnTelemetryUpdated;

        public bool EnableHardwareSensorPolling { get; set; } = true;

        private volatile bool _isDisposed;
        private string _targetExeName = string.Empty;
        private int _targetProcessId = 0;

        private CancellationTokenSource? _samplerCts;
        private Task? _samplerTask;

        public TelemetryHub()
        {
            StartSamplingLoop();
        }

        public void LockTarget(string executableName)
        {
            _targetExeName = executableName ?? string.Empty;
            _targetProcessId = 0;
            PresentMonCaptureService.Instance.Start(0, _targetExeName);
        }

        public void WakeUp(int processId, string executableName)
        {
            _targetProcessId = processId;
            _targetExeName = executableName ?? string.Empty;

            PresentMonCaptureService.Instance.Start(processId, executableName);

            CurrentSnapshot = CurrentSnapshot with
            {
                ActiveEngine = "PresentMon Core",
                IsLive = true
            };
            OnTelemetryUpdated?.Invoke(CurrentSnapshot);
        }

        public void Sleep()
        {
            _targetProcessId = 0;
            PresentMonCaptureService.Instance.Stop();

            CurrentSnapshot = new TelemetrySnapshot(0, 0, 0, 0, 0.0, "STANDBY", false);
            OnTelemetryUpdated?.Invoke(CurrentSnapshot);
        }

        private void StartSamplingLoop()
        {
            if (_samplerTask != null && !_samplerTask.IsCompleted) return;

            _samplerCts = new CancellationTokenSource();
            _samplerTask = Task.Run(() => SamplingWorkerAsync(_samplerCts.Token));
        }

        private async Task SamplingWorkerAsync(CancellationToken token)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));

            while (!token.IsCancellationRequested && await timer.WaitForNextTickAsync(token))
            {
                try
                {
                    PollMetrics();
                }
                catch { }
            }
        }

        public void PollMetrics()
        {
            var pm = PresentMonCaptureService.Instance;

            // 1. Query Hardware GPU Sensor Metrics via ADL if enabled
            double gpuActivity = 0;
            if (EnableHardwareSensorPolling && AmdAdlService.Instance.IsAvailable)
            {
                var adl = AmdAdlService.Instance.PollMetrics(_targetProcessId);
                gpuActivity = adl.GpuLoad;
            }

            // 2. Authoritative PresentMon Stream
            bool isRtssActive = AppUI.Services.Pacing.PacingIpcService.Instance.IsExternalLimiterActive;
            if (pm.IsActive && (DateTime.UtcNow - pm.LastSampleTime).TotalMilliseconds < 2000 && pm.CurrentFps > 0)
            {
                CurrentSnapshot = new TelemetrySnapshot(
                    pm.CurrentFps,
                    pm.FrametimeMs,
                    pm.Low1PercentFps,
                    pm.PacingJitterPct,
                    gpuActivity,
                    "PresentMon Core",
                    true,
                    pm.CadenceRatio,
                    pm.SubFrameVarianceUs,
                    pm.FrametimeMs,
                    isRtssActive
                );
                OnTelemetryUpdated?.Invoke(CurrentSnapshot);
                return;
            }

            // 3. Standby State
            if (CurrentSnapshot.IsLive && _targetProcessId == 0)
            {
                CurrentSnapshot = new TelemetrySnapshot(0, 0, 0, 0, 0.0, "STANDBY", false, 0.50, 0.0, 0.0, isRtssActive);
                OnTelemetryUpdated?.Invoke(CurrentSnapshot);
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _samplerCts?.Cancel();
            PresentMonCaptureService.Instance.Dispose();
        }
    }
}
