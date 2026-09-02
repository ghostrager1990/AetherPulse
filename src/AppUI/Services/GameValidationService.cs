using System;

namespace AppUI.Services
{
    public class GameValidationResult
    {
        public bool IsDx12Supported { get; set; } = true;
        public bool HasDlssOrStreamline { get; set; } = true;
        public bool IsFullyCompatible { get; set; } = true;
        public string FailureReason { get; set; } = string.Empty;
        public string DetectedDxVersion { get; set; } = "Direct3D 12";
        public string DetectedDllName { get; set; } = "nvngx_dlss.dll";
    }

    public static class GameValidationService
    {
        public static GameValidationResult ValidateGame(string exePath)
        {
            return new GameValidationResult();
        }
    }
}