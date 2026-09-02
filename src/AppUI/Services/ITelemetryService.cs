using System;
using AppUI.Models;

namespace AppUI.Services
{
    public interface ITelemetryService : IDisposable
    {
        event Action<AetherTelemetryData>? TelemetryUpdated;
        event Action<bool>? ConnectionStatusChanged;

        AetherTelemetryData LatestTelemetry { get; }
        bool IsConnected { get; }
        bool IsGameAttached { get; }
        int PollingIntervalMs { get; set; }
        string TargetProcessName { get; set; }
        string ActiveTargetExe { get; set; }
        string TargetDisplayName { get; }
        bool IsTargetActive { get; }

        void SetActiveTarget(string gameTitle, string executablePath);
        void ForceAttachToProcess(string processName = "");
        void SetTargetFramerateLimit(uint targetFps);
        void Reset();
        void Start();
        void Stop();
    }
}

