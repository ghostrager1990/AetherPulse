using System;
using System.IO;
using System.Text.Json;

namespace AppUI.Services
{
    public class AetherStatusPayload
    {
        public uint pid { get; set; }
        public ulong frames { get; set; }
        public ulong timestamp { get; set; }
        public bool pacing { get; set; }
        public bool rayRegen { get; set; }
        public double frametimeMs { get; set; }
        public double onePercentLowFps { get; set; }
        public double stutterPercent { get; set; }
    }

    public class HeartbeatWatcherService
    {
        public const string StatusFilePath = @"C:\Users\Public\aetherpulse_status.json";

        public static (bool IsActive, ulong FrameCount, uint ProcessId, bool IsRayRegen, bool IsPacing, double FrametimeMs, double OnePercentLowFps, double StutterPercent) CheckHeartbeat(ulong lastFrameCount)
        {
            if (!File.Exists(StatusFilePath))
            {
                return (false, 0, 0, false, false, 0, 0, 0);
            }

            try
            {
                using var fs = new FileStream(StatusFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs);
                string json = reader.ReadToEnd();
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var data = JsonSerializer.Deserialize<AetherStatusPayload>(json);
                    if (data != null)
                    {
                        ulong currentTick = (ulong)Environment.TickCount64;
                        bool isActuallyConnected = (currentTick >= data.timestamp ? currentTick - data.timestamp : 0) < 600 && data.frames > 0;
                        return (isActuallyConnected, data.frames, data.pid, data.rayRegen, data.pacing, data.frametimeMs, data.onePercentLowFps, data.stutterPercent);
                    }
                }
            }
            catch
            {
            }

            return (false, 0, 0, false, false, 0, 0, 0);
        }
    }
}
