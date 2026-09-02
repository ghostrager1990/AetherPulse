using System;
using System.Collections.Generic;

namespace AppUI.Models
{
    public enum DeploymentMode
    {
        DxgiProxy,
        StreamlineInterposer,
        FidelityFxNative,
        VersionProxy,
        DxcoreProxy,
        Both
    }

    public enum DeploymentStatus
    {
        Success,
        TargetNotFound,
        AccessDenied,
        ElevationRequired,
        FileLocked,
        Failed
    }

    public class DeploymentResult
    {
        public DeploymentStatus Status { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<string> DeployedFiles { get; set; } = new();
    }

    public class BackupManifest
    {
        public DateTime InstalledAt { get; set; } = DateTime.UtcNow;
        public List<string> InjectedFiles { get; set; } = new();
        public Dictionary<string, string> OriginalFileHashes { get; set; } = new();
    }
}


