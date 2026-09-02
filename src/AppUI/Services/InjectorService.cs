using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AppUI.Services
{
    public interface IInjectorService
    {
        void StartWatcher(string? targetExeName = null);
        void StopWatcher();
        bool Inject(int processId);
        bool IsInjected(int processId);
    }

    public class InjectorService : IInjectorService, IDisposable
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out UIntPtr lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, IntPtr lpThreadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetExitCodeThread(IntPtr hThread, out uint lpExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint PROCESS_ALL_ACCESS = 0x1F0FFF;
        private const uint PROCESS_CREATE_THREAD = 0x0002;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const uint PROCESS_VM_OPERATION = 0x0008;
        private const uint PROCESS_VM_WRITE = 0x0020;
        private const uint PROCESS_VM_READ = 0x0010;
        private const uint STANDARD_INJECT_ACCESS = PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION | PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ;

        private const uint MEM_COMMIT = 0x00001000;
        private const uint MEM_RESERVE = 0x00002000;
        private const uint PAGE_READWRITE = 0x04;
        private const uint WAIT_TIMEOUT = 0x00000102;
        private const uint INFINITE = 0xFFFFFFFF;

        private readonly ConcurrentDictionary<int, DateTime> _injectedPids = new();
        private CancellationTokenSource? _cts;
        private Task? _watcherTask;
        private string? _targetExe;

        public static string LastStatusMessage { get; private set; } = "Standby (Waiting for Game Injection)";
        public static bool IsLastInjectionSuccessful { get; private set; } = false;

        public void StartWatcher(string? targetExeName = null)
        {
            StopWatcher();
            _targetExe = targetExeName;
            _cts = new CancellationTokenSource();
            _watcherTask = Task.Run(() => WatcherLoop(_cts.Token), _cts.Token);
        }

        public void StopWatcher()
        {
            _cts?.Cancel();
            try { _watcherTask?.Wait(500); } catch { }
            _cts?.Dispose();
            _cts = null;
            _watcherTask = null;
        }

        public bool IsInjected(int processId) => _injectedPids.ContainsKey(processId);

        public bool Inject(int processId)
        {
            if (_injectedPids.ContainsKey(processId)) return true;

            // 1. Purge any lingering proxy files in the game's directory
            try
            {
                using var proc = Process.GetProcessById(processId);
                string? exePath = proc.MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                {
                    string gameFolder = Path.GetDirectoryName(exePath)!;
                    GameBackupService.CleanGameDirectory(gameFolder);
                }
            }
            catch { }

            // 2. Locate native payload
            string? dllPath = FindCoreDllPath();
            if (string.IsNullOrEmpty(dllPath) || !File.Exists(dllPath))
            {
                LastStatusMessage = "ERROR: AetherPulseCore.dll not found on disk.";
                Debug.WriteLine($"[AetherPulse Injector] {LastStatusMessage}");
                return false;
            }

            // 3. Open target process
            IntPtr hProcess = OpenProcess(STANDARD_INJECT_ACCESS, false, processId);
            if (hProcess == IntPtr.Zero)
            {
                hProcess = OpenProcess(PROCESS_ALL_ACCESS, false, processId);
            }

            if (hProcess == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                LastStatusMessage = $"ERROR: OpenProcess(PID {processId}) failed with Win32 error {err}. Run as Administrator.";
                Debug.WriteLine($"[AetherPulse Injector] {LastStatusMessage}");
                return false;
            }

            try
            {
                byte[] dllBytes = Encoding.Unicode.GetBytes(dllPath + "\0");
                IntPtr allocMem = VirtualAllocEx(hProcess, IntPtr.Zero, (uint)dllBytes.Length, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
                if (allocMem == IntPtr.Zero)
                {
                    int err = Marshal.GetLastWin32Error();
                    LastStatusMessage = $"ERROR: VirtualAllocEx failed with Win32 error {err}.";
                    Debug.WriteLine($"[AetherPulse Injector] {LastStatusMessage}");
                    return false;
                }

                if (!WriteProcessMemory(hProcess, allocMem, dllBytes, (uint)dllBytes.Length, out _))
                {
                    int err = Marshal.GetLastWin32Error();
                    LastStatusMessage = $"ERROR: WriteProcessMemory failed with Win32 error {err}.";
                    Debug.WriteLine($"[AetherPulse Injector] {LastStatusMessage}");
                    return false;
                }

                IntPtr hKernel32 = GetModuleHandle("kernel32.dll");
                if (hKernel32 == IntPtr.Zero) return false;

                IntPtr loadLibraryAddr = GetProcAddress(hKernel32, "LoadLibraryW");
                if (loadLibraryAddr == IntPtr.Zero) return false;

                IntPtr hThread = CreateRemoteThread(hProcess, IntPtr.Zero, 0, loadLibraryAddr, allocMem, 0, IntPtr.Zero);
                if (hThread == IntPtr.Zero)
                {
                    int err = Marshal.GetLastWin32Error();
                    LastStatusMessage = $"ERROR: CreateRemoteThread failed with Win32 error {err}.";
                    Debug.WriteLine($"[AetherPulse Injector] {LastStatusMessage}");
                    return false;
                }

                // Wait up to 2000ms for LoadLibraryW to complete in target process
                uint waitRes = WaitForSingleObject(hThread, 2000);
                if (waitRes == 0)
                {
                    GetExitCodeThread(hThread, out uint exitCode);
                    if (exitCode != 0)
                    {
                        LastStatusMessage = $"LIVE (D3D12 Hook Attached: 0x{exitCode:X8})";
                        IsLastInjectionSuccessful = true;
                        Debug.WriteLine($"[AetherPulse Injector] Payload loaded successfully in PID {processId}, remote HMODULE: 0x{exitCode:X8}");
                    }
                    else
                    {
                        LastStatusMessage = "WARNING: Remote LoadLibraryW returned NULL handle.";
                        Debug.WriteLine($"[AetherPulse Injector] {LastStatusMessage}");
                    }
                }
                else
                {
                    LastStatusMessage = "LIVE (Remote Thread Dispatched)";
                    IsLastInjectionSuccessful = true;
                }

                CloseHandle(hThread);
                _injectedPids[processId] = DateTime.UtcNow;
                return true;
            }
            finally
            {
                CloseHandle(hProcess);
            }
        }

        private async Task WatcherLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var processes = Process.GetProcesses();
                    foreach (var proc in processes)
                    {
                        try
                        {
                            if (proc.Id <= 4) continue;
                            if (_injectedPids.ContainsKey(proc.Id)) continue;

                            bool isTarget = false;
                            if (!string.IsNullOrEmpty(_targetExe))
                            {
                                isTarget = proc.ProcessName.Equals(_targetExe, StringComparison.OrdinalIgnoreCase) ||
                                           $"{proc.ProcessName}.exe".Equals(_targetExe, StringComparison.OrdinalIgnoreCase);
                            }
                            else
                            {
                                isTarget = proc.MainWindowHandle != IntPtr.Zero && !IsSystemProcess(proc.ProcessName);
                            }

                            if (isTarget && proc.MainWindowHandle != IntPtr.Zero)
                            {
                                Inject(proc.Id);
                            }
                        }
                        catch { }
                        finally
                        {
                            proc.Dispose();
                        }
                    }
                }
                catch { }

                await Task.Delay(1000, token);
            }
        }

        private static bool IsSystemProcess(string name)
        {
            string lower = name.ToLowerInvariant();
            return lower is "explorer" or "devenv" or "code" or "taskmgr" or "svchost" or "dwm" or "csrss" or "smss" or "lsass" or "services" or "aetherpulse" or "dotnet";
        }

        private static string? FindCoreDllPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates = new[]
            {
                Path.Combine(baseDir, "Redist", "AetherPulseCore.dll"),
                Path.Combine(baseDir, "AetherPulseCore.dll"),
                @"G:\Antigravity Projects\AetherPulse-v1.2.0\src\AppUI\Redist\AetherPulseCore.dll",
                @"G:\Antigravity Projects\AetherPulse-v1.2.0\src\NativeCore\build\Release\AetherPulseCore.dll"
            };

            foreach (var path in candidates)
            {
                if (File.Exists(path)) return Path.GetFullPath(path);
            }
            return null;
        }

        public void Dispose()
        {
            StopWatcher();
        }
    }
}
