using System;
using System.Runtime.InteropServices;
using System.Text;

namespace AppUI.Models
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct AetherTelemetryData
    {
        public uint Sequence;
        public uint StructVersion;
        public uint FrameIndex;
        public float CurrentFps;
        public float AverageFps;
        public float FrameTimeMs;
        public float PacingJitterMs;
        public uint DroppedFrames;
        public byte IsPacerActive;
        public byte IsRayRegenActive;
        public uint ActiveDenoiserFlags;
        public float CadenceRatio;
        public float SubFrameVarianceUs;
        public float RealTimeDeltaMs;
        public byte IsExternalLimiterActive;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
        public byte[] RawGameTitle;

        public bool PacerActive => IsPacerActive != 0;
        public bool RayRegenActive => IsRayRegenActive != 0;
        public bool ExternalLimiterActive => IsExternalLimiterActive != 0;

        public string ActiveGameTitle
        {
            get
            {
                if (RawGameTitle == null || RawGameTitle.Length == 0) return string.Empty;
                int nullIdx = Array.IndexOf(RawGameTitle, (byte)0);
                int count = (nullIdx >= 0) ? nullIdx : RawGameTitle.Length;
                return Encoding.ASCII.GetString(RawGameTitle, 0, count).Trim();
            }
        }

        public static AetherTelemetryData Empty => new()
        {
            StructVersion = 1,
            CurrentFps = 0.0f,
            AverageFps = 0.0f,
            FrameTimeMs = 0.0f,
            PacingJitterMs = 0.0f,
            IsPacerActive = 0,
            IsRayRegenActive = 0,
            ActiveDenoiserFlags = 0,
            CadenceRatio = 0.5f,
            SubFrameVarianceUs = 0.0f,
            RealTimeDeltaMs = 0.0f,
            IsExternalLimiterActive = 0,
            DroppedFrames = 0,
            RawGameTitle = new byte[128]
        };
    }
}
