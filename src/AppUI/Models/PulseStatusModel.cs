namespace AppUI.Models
{
    public class PulseStatusModel
    {
        public uint ProcessId { get; set; }
        public ulong FrameCount { get; set; }
        public ulong TimestampMs { get; set; }
        public bool IsPacingActive { get; set; }
        public bool IsRayRegenActive { get; set; }
        public double FrametimeMs { get; set; }
        public double OnePercentLowFps { get; set; }
        public double StutterPercent { get; set; }
    }
}
