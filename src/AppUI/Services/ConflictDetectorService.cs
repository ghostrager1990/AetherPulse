using System;
using System.Collections.Generic;
using System.IO;

namespace AppUI.Services
{
    public class ConflictScanResult
    {
        public bool HasOptiScalerConflict { get; set; }
        public List<string> ConflictingFiles { get; set; } = new();
        public string WarningMessage { get; set; } = string.Empty;
    }

    public interface IConflictDetectorService
    {
        ConflictScanResult ScanForConflicts(string gameDirectory);
        bool CleanOptiScalerArtifacts(string gameDirectory, out List<string> removedFiles);
    }

    public class ConflictDetectorService : IConflictDetectorService
    {
        public ConflictScanResult ScanForConflicts(string gameDirectory)
        {
            var result = new ConflictScanResult();
            // In Option A architecture, OptiScaler is the managed payload backend.
            // No conflict warning is triggered for managed payload files.
            return result;
        }

        public bool CleanOptiScalerArtifacts(string gameDirectory, out List<string> removedFiles)
        {
            removedFiles = new List<string>();
            return true;
        }
    }
}
