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
        bool IsGameAttached { get; }
        int PollingIntervalMs { get; set; }
        string TargetProcessName { get; set; }
        string TargetDisplayName { get; }
        bool IsTargetActive { get; }

        void SetActiveTarget(string gameTitle, string executablePath);
        void ForceAttachToProcess(string processName = "CrimsonDesert");
        void Start();
        void Stop();
    }
}
