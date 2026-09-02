using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace AppUI.Services.Telemetry
{
    public class PresentMonCaptureService : IDisposable
    {
        public static PresentMonCaptureService Instance { get; } = new();

        private Process? _presentMonProc;
        private static readonly object _syncLock = new();
        private readonly ConcurrentQueue<double> _frametimes = new();
                private int _frametimeColIdx = -1;
        private int _appColIdx = 0;
        private static readonly HashSet<string> IgnoredProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "dwm.exe", "dwm",
            "explorer.exe", "explorer",
            "devenv.exe", "devenv",
            "aetherpulse.exe", "aetherpulse",
            "chrome.exe", "chrome",
            "msedge.exe", "msedge",
            "firefox.exe", "firefox",
            "brave.exe", "brave",
            "applicationframehost.exe", "applicationframehost",
            "searchhost.exe", "searchhost",
            "startmenuexperiencehost.exe", "startmenuexperiencehost",
            "shellexperiencehost.exe", "shellexperiencehost",
            "textinputhost.exe", "textinputhost",
            "systemsettings.exe", "systemsettings",
            "taskmgr.exe", "taskmgr"
        };
        private bool _isDisposed;

        public static void ResetTelemetryBuffer()
        {
            lock (_syncLock)
            {
                Instance._frametimes.Clear();
            }
        }

        public double CurrentFps { get; private set; }
        public double FrametimeMs { get; private set; }
        public double Low1PercentFps { get; private set; }
        public double PacingJitterPct { get; private set; }
        public double CadenceRatio { get; private set; } = 0.50;
        public double SubFrameVarianceUs { get; private set; }
        public bool IsActive { get; private set; }
        public DateTime LastSampleTime { get; private set; } = DateTime.MinValue;

        public void Start(int processId, string? processName)
        {
            Stop();

            string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "PresentMon.exe");
            if (!File.Exists(exePath))
            {
                string altPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PresentMon.exe");
                if (File.Exists(altPath))
                {
                    exePath = altPath;
                }
                else
                {
                    Debug.WriteLine($"[PresentMon] Executable not found at {exePath}");
                    return;
                }
            }

            string cleanName = Path.GetFileNameWithoutExtension(processName ?? "");
            string targetParam = (processId > 0)
                ? $"--process_id {processId}"
                : (!string.IsNullOrEmpty(cleanName) && !cleanName.Equals("Auto (All Detected)", StringComparison.OrdinalIgnoreCase)
                    ? $"--process_name {cleanName}.exe"
                    : "");

            string args = string.IsNullOrWhiteSpace(targetParam)
                ? "--output_stdout --no_console_stats --exclude_dropped --v1_metrics --stop_existing_session"
                : $"--output_stdout --no_console_stats --exclude_dropped --v1_metrics --stop_existing_session {targetParam}";

            try
            {
                _presentMonProc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = args,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        CreateNoWindow = true,
                        WorkingDirectory = Path.GetDirectoryName(exePath)
                    },
                    EnableRaisingEvents = true
                };

                _presentMonProc.OutputDataReceived += OnOutputDataReceived;
                _presentMonProc.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        Debug.WriteLine($"[PresentMon STDERR] {e.Data}");
                    }
                };

                _presentMonProc.Start();
                _presentMonProc.BeginOutputReadLine();
                _presentMonProc.BeginErrorReadLine();
                IsActive = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PresentMon Startup Error] {ex.Message}");
                IsActive = false;
            }
        }

        private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;

            string line = e.Data.Trim();

            // Detect column headers
            if (line.StartsWith("Application", StringComparison.OrdinalIgnoreCase) || 
                line.StartsWith("ProcessID", StringComparison.OrdinalIgnoreCase))
            {
                var headers = line.Split(',').Select(h => h.Trim()).ToList();
                _frametimeColIdx = headers.FindIndex(h => 
                    h.Equals("MsBetweenDisplayChange", StringComparison.OrdinalIgnoreCase) ||
                    h.Equals("MsBetweenPresents", StringComparison.OrdinalIgnoreCase) ||
                    h.Equals("msBetweenPresents", StringComparison.OrdinalIgnoreCase));
                return;
            }

            // Parse sample
            var parts = line.Split(',');
            int colIdx = _frametimeColIdx >= 0 ? _frametimeColIdx : 9;

            // In Auto mode, ignore presentations from OS compositor & desktop apps
            if (parts.Length > _appColIdx && _appColIdx >= 0)
            {
                string app = parts[_appColIdx].Trim();
                if (IgnoredProcesses.Contains(app))
                {
                    return;
                }
            }

            if (parts.Length > colIdx)
            {
                string val = parts[colIdx].Trim();
                if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double ftMs))
                {
                    // Filter out invalid/sleep markers
                    if (ftMs >= 1.0 && ftMs <= 200.0)
                    {
                        RecordFrame(ftMs);
                    }
                }
            }
        }

        private void RecordFrame(double frametimeMs)
        {
            LastSampleTime = DateTime.UtcNow;

            double[] samples;
            lock (_syncLock)
            {
                _frametimes.Enqueue(frametimeMs);
                while (_frametimes.Count > 4)
                {
                    _frametimes.TryDequeue(out _);
                }
                samples = _frametimes.ToArray();
            }

            if (samples.Length == 0) return;

            // Average frametime over the rolling window (eliminates FSR 3 / AFMF presentation cadence gaps)
            double avgFrametime = samples.Average();
            FrametimeMs = avgFrametime;
            CurrentFps = avgFrametime > 0 ? (1000.0 / avgFrametime) : 0.0;

            // 1% Low computation (99th percentile frametime converted to FPS floor)
            var sortedAsc = samples.OrderBy(x => x).ToArray(); // [Fastest ms ... Slowest ms]
            int p99Index = Math.Min(sortedAsc.Length - 1, (int)Math.Ceiling(sortedAsc.Length * 0.99) - 1);
            p99Index = Math.Max(0, p99Index);

            double worstFrametimeMs = sortedAsc[p99Index];
            Low1PercentFps = worstFrametimeMs > 0 ? (1000.0 / worstFrametimeMs) : CurrentFps;

            // Ensure 1% Low is clamped so it never exceeds Average FPS due to sample jitter
            if (Low1PercentFps > CurrentFps)
            {
                Low1PercentFps = CurrentFps;
            }

            // Pacing Jitter computation & Cadence Ratio
            double variance = samples.Select(v => Math.Abs(v - avgFrametime)).Average();
            PacingJitterPct = avgFrametime > 0 ? (variance / avgFrametime) * 100.0 : 0.0;
            SubFrameVarianceUs = variance * 1000.0;

            if (samples.Length >= 2)
            {
                double lastSample = samples[^1];
                double prevSample = samples[^2];
                double total = lastSample + prevSample;
                CadenceRatio = total > 0.001 ? (lastSample / total) : 0.50;
            }
            else
            {
                CadenceRatio = 0.50;
            }
        }

        public static void Shutdown()
        {
            try
            {
                Instance.Stop();
                foreach (var proc in Process.GetProcessesByName("PresentMon"))
                {
                    try
                    {
                        proc.Kill(entireProcessTree: true);
                        proc.Dispose();
                    }
                    catch { }
                }
            }
            catch { }
        }

        public void Stop()
        {
            try
            {
                if (_presentMonProc != null && !_presentMonProc.HasExited)
                {
                    _presentMonProc.Kill(entireProcessTree: true);
                    _presentMonProc.Dispose();
                }
            }
            catch { }
            finally
            {
                _presentMonProc = null;
                _frametimeColIdx = -1;
                _frametimes.Clear();
                IsActive = false;
                CurrentFps = 0;
                FrametimeMs = 0;
                Low1PercentFps = 0;
                PacingJitterPct = 0;
                CadenceRatio = 0.50;
                SubFrameVarianceUs = 0.0;
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            Stop();
        }
    }
}
