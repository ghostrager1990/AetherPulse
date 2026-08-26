using System;

namespace AppUI.Models
{
    [Flags]
    public enum HookType
    {
        None = 0,
        Dxgi = 1 << 0,
        Streamline = 1 << 1,
        AetherPulseCore = 1 << 2
    }

    public class GameProcessInfo
    {
        public int ProcessId { get; init; }
        public string ProcessName { get; init; } = string.Empty;
        public string ExecutablePath { get; init; } = string.Empty;
        public string MainWindowTitle { get; init; } = string.Empty;
        public DateTime StartTime { get; init; }
        public HookType ActiveHooks { get; set; } = HookType.None;
        public bool IsHooked => ActiveHooks != HookType.None;
    }
}
