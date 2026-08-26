using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AppUI.Models;

namespace AppUI.Services
{
    public class ProcessWatcherService : IProcessWatcherService
    {
        private readonly ConcurrentDictionary<string, byte> _watchedNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<int, GameProcessInfo> _activeProcesses = new();
        private readonly object _lock = new();

        private CancellationTokenSource? _cts;
        private Task? _watcherTask;
        private int _checkIntervalMs = 1000;

        public event EventHandler<GameProcessInfo>? OnGameStarted;
        public event EventHandler<GameProcessInfo>? OnGameStopped;
        public event EventHandler<GameProcessInfo>? OnGameUpdated;

        public IReadOnlyDictionary<int, GameProcessInfo> ActiveWatchedProcesses => _activeProcesses;
        public bool IsRunning => _watcherTask != null && !_watcherTask.IsCompleted;

        public void RegisterTarget(string processOrExecutableName)
        {
            if (string.IsNullOrWhiteSpace(processOrExecutableName)) return;
            string cleanName = Path.GetFileNameWithoutExtension(processOrExecutableName).Trim();
            _watchedNames.TryAdd(cleanName, 0);
        }

        public void UnregisterTarget(string processOrExecutableName)
        {
            if (string.IsNullOrWhiteSpace(processOrExecutableName)) return;
            string cleanName = Path.GetFileNameWithoutExtension(processOrExecutableName).Trim();
            _watchedNames.TryRemove(cleanName, out _);
        }

        public void SetTargets(IEnumerable<string> targets)
        {
            _watchedNames.Clear();
            foreach (var t in targets)
            {
                RegisterTarget(t);
            }
        }

        public void Start()
        {
            lock (_lock)
            {
                if (IsRunning) return;
                _cts = new CancellationTokenSource();
                _watcherTask = Task.Run(() => WatcherLoopAsync(_cts.Token));
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
                _watcherTask?.Wait(1000);
            }
            catch (AggregateException)
            {
            }

            _activeProcesses.Clear();
        }

        private async Task WatcherLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    CheckProcesses();
                }
                catch (Exception)
                {
                    // Ignore transient process enumeration errors
                }

                try
                {
                    await Task.Delay(_checkIntervalMs, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private void CheckProcesses()
        {
            if (_watchedNames.IsEmpty) return;

            Process[] runningProcesses = Process.GetProcesses();
            var currentPids = new HashSet<int>();

            foreach (var process in runningProcesses)
            {
                try
                {
                    string processName = process.ProcessName;
                    if (!_watchedNames.ContainsKey(processName))
                    {
                        continue;
                    }

                    int pid = process.Id;
                    currentPids.Add(pid);

                    if (!_activeProcesses.TryGetValue(pid, out var existingInfo))
                    {
                        string exePath = string.Empty;
                        try
                        {
                            exePath = process.MainModule?.FileName ?? string.Empty;
                        }
                        catch (Win32Exception)
                        {
                            // Access denied for elevated processes if watcher isn't elevated
                        }

                        var newInfo = new GameProcessInfo
                        {
                            ProcessId = pid,
                            ProcessName = processName,
                            ExecutablePath = exePath,
                            MainWindowTitle = process.MainWindowTitle,
                            StartTime = TryGetStartTime(process),
                            ActiveHooks = DetectActiveHooks(process)
                        };

                        if (_activeProcesses.TryAdd(pid, newInfo))
                        {
                            OnGameStarted?.Invoke(this, newInfo);
                        }
                    }
                    else
                    {
                        // Refresh hooks detection if previously unhooked
                        if (existingInfo.ActiveHooks == HookType.None)
                        {
                            var updatedHooks = DetectActiveHooks(process);
                            if (updatedHooks != existingInfo.ActiveHooks)
                            {
                                existingInfo.ActiveHooks = updatedHooks;
                                OnGameUpdated?.Invoke(this, existingInfo);
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // Process may have exited during inspection
                }
                finally
                {
                    process.Dispose();
                }
            }

            // Detect exited processes
            var terminatedPids = _activeProcesses.Keys.Where(pid => !currentPids.Contains(pid)).ToList();
            foreach (var pid in terminatedPids)
            {
                if (_activeProcesses.TryRemove(pid, out var removedInfo))
                {
                    OnGameStopped?.Invoke(this, removedInfo);
                }
            }
        }

        private static DateTime TryGetStartTime(Process process)
        {
            try
            {
                return process.StartTime;
            }
            catch
            {
                return DateTime.Now;
            }
        }

        private static HookType DetectActiveHooks(Process process)
        {
            var hooks = HookType.None;
            try
            {
                foreach (ProcessModule module in process.Modules)
                {
                    string moduleName = module.ModuleName;
                    if (string.Equals(moduleName, "AetherPulseCore.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        hooks |= HookType.AetherPulseCore;
                    }
                    else if (string.Equals(moduleName, "dxgi.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        hooks |= HookType.Dxgi;
                    }
                    else if (string.Equals(moduleName, "sl.interposer.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        hooks |= HookType.Streamline;
                    }
                }
            }
            catch (Win32Exception)
            {
                // Module list query failed due to access rights
            }
            catch (Exception)
            {
            }

            return hooks;
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
