using System;
using System.Runtime.InteropServices;
using System.Text;

namespace AppUI.Models
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct AetherTelemetryData
    {
        public uint StructVersion;
        public float CurrentFps;
        public float FrameTimeMs;
        public float PacingJitterMs;
        [MarshalAs(UnmanagedType.I1)]
        public bool IsPacerActive;
        [MarshalAs(UnmanagedType.I1)]
        public bool IsRayRegenActive;
        public uint ActiveDenoiserFlags;
        public uint DroppedFrames;
        public fixed byte RawGameTitle[128];

        public string ActiveGameTitle
        {
            get
            {
                fixed (byte* p = RawGameTitle)
                {
                    return Marshal.PtrToStringAnsi((IntPtr)p) ?? string.Empty;
                }
            }
            set
            {
                fixed (byte* p = RawGameTitle)
                {
                    byte[] bytes = Encoding.ASCII.GetBytes(value ?? string.Empty);
                    int len = Math.Min(bytes.Length, 127);
                    for (int i = 0; i < len; i++)
                    {
                        p[i] = bytes[i];
                    }
                    for (int i = len; i < 128; i++)
                    {
                        p[i] = 0;
                    }
                }
            }
        }

        public static AetherTelemetryData Empty
        {
            get
            {
                var data = new AetherTelemetryData
                {
                    StructVersion = 1,
                    CurrentFps = 0.0f,
                    FrameTimeMs = 0.0f,
                    PacingJitterMs = 0.0f,
                    IsPacerActive = false,
                    IsRayRegenActive = false,
                    ActiveDenoiserFlags = 0,
                    DroppedFrames = 0
                };
                data.ActiveGameTitle = string.Empty;
                return data;
            }
        }
    }
}
