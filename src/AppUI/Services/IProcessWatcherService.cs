using System;
using System.Collections.Generic;
using AppUI.Models;

namespace AppUI.Services
{
    public interface IProcessWatcherService : IDisposable
    {
        event EventHandler<GameProcessInfo>? OnGameStarted;
        event EventHandler<GameProcessInfo>? OnGameStopped;
        event EventHandler<GameProcessInfo>? OnGameUpdated;

        IReadOnlyDictionary<int, GameProcessInfo> ActiveWatchedProcesses { get; }
        bool IsRunning { get; }

        void RegisterTarget(string processOrExecutableName);
        void UnregisterTarget(string processOrExecutableName);
        void SetTargets(IEnumerable<string> targets);

        void Start();
        void Stop();
    }
}
