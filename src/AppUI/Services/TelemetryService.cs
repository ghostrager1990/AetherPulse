using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AppUI.Models;

namespace AppUI.Services
{
    public class TelemetryService : ITelemetryService
    {
        public const string MemoryMapName = @"Local\AetherPulseTelemetry";

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

        public AetherTelemetryData LatestTelemetry
        {
            get
            {
                lock (_lock)
                {
                    return _latestTelemetry;
                }
            }
            private set
            {
                lock (_lock)
                {
                    _latestTelemetry = value;
                }
            }
        }

        public bool IsConnected
        {
            get
            {
                lock (_lock)
                {
                    return _isConnected;
                }
            }
            private set
            {
                bool changed;
                lock (_lock)
                {
                    changed = _isConnected != value;
                    _isConnected = value;
                }

                if (changed)
                {
                    ConnectionStatusChanged?.Invoke(this, value);
                }
            }
        }

        public int PollingIntervalMs
        {
            get => _pollingIntervalMs;
            set => _pollingIntervalMs = Math.Max(1, value);
        }

        public void Start()
        {
            lock (_lock)
            {
                if (_pollingTask != null && !_pollingTask.IsCompleted)
                {
                    return;
                }

                _cts = new CancellationTokenSource();
                _pollingTask = Task.Run(() => PollingLoopAsync(_cts.Token));
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                _cts?.Cancel();
            }

            try
            {
                _pollingTask?.Wait(500);
            }
            catch (AggregateException)
            {
                // Ignore task cancellation exceptions on shutdown
            }

            CloseMemoryMap();
            IsConnected = false;
        }

        private async Task PollingLoopAsync(CancellationToken ct)
        {
            int structSize = Marshal.SizeOf<AetherTelemetryData>();

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_mmf == null || _accessor == null)
                    {
                        TryOpenMemoryMap();
                    }

                    if (_accessor != null)
                    {
                        _accessor.Read(0, out AetherTelemetryData data);

                        if (data.StructVersion == 1)
                        {
                            LatestTelemetry = data;
                            IsConnected = true;
                            TelemetryUpdated?.Invoke(this, data);
                        }
                    }
                    else
                    {
                        IsConnected = false;
                    }
                }
                catch (FileNotFoundException)
                {
                    // Memory map not yet created by game / native DLL
                    CloseMemoryMap();
                    IsConnected = false;
                }
                catch (Exception)
                {
                    CloseMemoryMap();
                    IsConnected = false;
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
                _mmf = MemoryMappedFile.OpenExisting(MemoryMapName, MemoryMappedFileRights.Read);
                _accessor = _mmf.CreateViewAccessor(0, Marshal.SizeOf<AetherTelemetryData>(), MemoryMappedFileAccess.Read);
            }
            catch
            {
                CloseMemoryMap();
            }
        }

        private void CloseMemoryMap()
        {
            _accessor?.Dispose();
            _accessor = null;

            _mmf?.Dispose();
            _mmf = null;
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
            _cts = null;
            GC.SuppressFinalize(this);
        }
    }
}
