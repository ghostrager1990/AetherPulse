using System;
using AppUI.Models;

namespace AppUI.Services
{
    public interface ITelemetryService : IDisposable
    {
        event EventHandler<AetherTelemetryData>? TelemetryUpdated;
        event EventHandler<bool>? ConnectionStatusChanged;

        AetherTelemetryData LatestTelemetry { get; }
        bool IsConnected { get; }
        int PollingIntervalMs { get; set; }

        void Start();
        void Stop();
    }
}
