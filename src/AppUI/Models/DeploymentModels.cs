using System;
using System.Collections.Generic;

namespace AppUI.Models
{
    public enum DeploymentMode
    {
        DxcoreProxy,           // Deploys as dxcore.dll (Default / Direct DX12 Core proxy)
        VersionProxy,          // Deploys as version.dll (Coexists with ReShade)
        DxgiProxy,             // Deploys as dxgi.dll (Direct DXGI hook)
        WinMMProxy,            // Deploys as winmm.dll (Alternative System proxy)
        StreamlineInterposer,  // Deploys as sl.interposer.dll
        Both                   // Deploys both dxcore.dll and sl.interposer.dll
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
