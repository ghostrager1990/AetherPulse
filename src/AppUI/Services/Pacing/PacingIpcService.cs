using System;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using AppUI.Services.Telemetry;

namespace AppUI.Services.Pacing
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct AetherPulsePacingIPC
    {
        public uint TargetFps;
        public uint MultiplierMode;
        public float LatencyToleranceMs;
        public float SpinWaitThresholdMs;
        public float MaxDriftMs;
        public byte EnablePacing;
        public byte IsHookActive;
        public byte AutoEma;
        public float ManualEmaAlpha;
        public byte IsExternalLimiterActive;
    }

    public class PacingIpcService
    {
        private static PacingIpcService? _instance;
        public static PacingIpcService Instance => _instance ??= new PacingIpcService();

        private MemoryMappedFile? _mmf;
        private MemoryMappedViewAccessor? _accessor;

        public bool IsExternalLimiterActive
        {
            get
            {
                var ipc = ReadCurrentIPC();
                if (ipc.IsExternalLimiterActive == 1) return true;
                
                // Only return true if RTSS has an active frame cap configured for Global or CrimsonDesert
                return false;
            }
        }

        public void Initialize()
        {
            try
            {
                // Use Global\ namespace to ensure cross-process and cross-integrity access between UI and game hook
                _mmf = MemoryMappedFile.CreateOrOpen("Global\\AetherPulse_Pacing_IPC", Marshal.SizeOf<AetherPulsePacingIPC>());
                _accessor = _mmf.CreateViewAccessor();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[IPC Init Error] {ex.Message}");
            }
        }

        public AetherPulsePacingIPC ReadCurrentIPC()
        {
            try
            {
                if (_accessor == null) Initialize();
                if (_accessor != null)
                {
                    _accessor.Read(0, out AetherPulsePacingIPC ipc);
                    return ipc;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[IPC Read Error] {ex.Message}");
            }
            return new AetherPulsePacingIPC();
        }

        public void UpdateIPC(Action<AetherPulsePacingIPC> updateAction)
        {
            try
            {
                if (_accessor == null) Initialize();
                if (_accessor != null)
                {
                    var current = ReadCurrentIPC();
                    updateAction(current);
                    _accessor.Write(0, ref current);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[IPC Write Error] {ex.Message}");
            }
        }

        public bool PollHookHandshake()
        {
            var ipc = ReadCurrentIPC();
            return ipc.IsHookActive == 1;
        }
                                    public void PushConfig(Action<AetherPulsePacingIPC> updateAction)
        {
            UpdateIPC(updateAction);
        }

        public void PushConfig(uint targetFps, uint multiplierMode, float latencyToleranceMs, float spinWaitThresholdMs, float maxDriftMs, bool enablePacing, bool autoEma, float manualEmaAlpha)
        {
            UpdateIPC(ipc => {
                ipc.TargetFps = targetFps;
                ipc.MultiplierMode = multiplierMode;
                ipc.LatencyToleranceMs = latencyToleranceMs;
                ipc.SpinWaitThresholdMs = spinWaitThresholdMs;
                ipc.MaxDriftMs = maxDriftMs;
                ipc.EnablePacing = (byte)(enablePacing ? 1 : 0);
                ipc.AutoEma = (byte)(autoEma ? 1 : 0);
                ipc.ManualEmaAlpha = manualEmaAlpha;
            });
        }

        public void PushConfig(uint targetFps, uint multiplierMode, float latencyToleranceMs, float spinWaitThresholdMs, float maxDriftMs, bool enablePacing)
        {
            UpdateIPC(ipc => {
                ipc.TargetFps = targetFps;
                ipc.MultiplierMode = multiplierMode;
                ipc.LatencyToleranceMs = latencyToleranceMs;
                ipc.SpinWaitThresholdMs = spinWaitThresholdMs;
                ipc.MaxDriftMs = maxDriftMs;
                ipc.EnablePacing = (byte)(enablePacing ? 1 : 0);
            });
        }
    }
}

