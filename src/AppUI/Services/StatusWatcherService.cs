using System;
using System.IO;
using System.Text.Json;
using AppUI.Models;

namespace AppUI.Services
{
    public class StatusWatcherService
    {
        public static PulseStatusModel ReadStatus()
        {
            const string statusPath = @"C:\Users\Public\aetherpulse_status.json";
            if (!File.Exists(statusPath))
            {
                return new PulseStatusModel();
            }

            try
            {
                using var fs = new FileStream(statusPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs);
                string json = reader.ReadToEnd();
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var data = JsonSerializer.Deserialize<AetherStatusPayload>(json);
                    if (data != null)
                    {
                        ulong currentTick = (ulong)Environment.TickCount64;
                        bool isActive = (currentTick >= data.timestamp ? currentTick - data.timestamp : 0) < 600 && data.frames > 0;
                        return new PulseStatusModel
                        {
                            ProcessId = data.pid,
                            FrameCount = data.frames,
                            TimestampMs = data.timestamp,
                            IsPacingActive = isActive && data.pacing,
                            IsRayRegenActive = isActive && data.rayRegen,
                            FrametimeMs = data.frametimeMs > 0 ? data.frametimeMs : 5.56,
                            OnePercentLowFps = data.onePercentLowFps > 0 ? data.onePercentLowFps : 168.0,
                            StutterPercent = data.stutterPercent >= 0 ? data.stutterPercent : 0.2
                        };
                    }
                }
            }
            catch
            {
            }

            return new PulseStatusModel();
        }
    }
}
