using System;
using System.Collections.Generic;

namespace AppUI.Models
{
    public enum DeploymentMode
    {
        DxgiProxy,             // Deploys as dxgi.dll
        StreamlineInterposer,  // Deploys as sl.interposer.dll
        Both                   // Deploys both dxgi.dll and sl.interposer.dll
    }

    public enum DeploymentStatus
    {
        Success,
        AlreadyInstalled,
        TargetNotFound,
        AccessDenied,
        FileLocked,
        ElevationRequired,
        Failed
    }

    public class DeploymentResult
    {
        public DeploymentStatus Status { get; init; }
        public string Message { get; init; } = string.Empty;
        public List<string> DeployedFiles { get; init; } = new();
        public List<string> BackupFiles { get; init; } = new();
        public bool Succeeded => Status == DeploymentStatus.Success || Status == DeploymentStatus.AlreadyInstalled;
    }
}
