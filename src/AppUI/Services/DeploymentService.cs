using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AppUI.Models;

namespace AppUI.Services
{
    public interface IDeploymentService
    {
        bool IsDeployed(string executablePath, DeploymentMode mode);
        Task<DeploymentResult> DeployAsync(string executablePath, DeploymentMode mode, string? customIniPath = null);
        Task<DeploymentResult> UninstallAsync(string executablePath, DeploymentMode mode);
        string? FindDxgiDllPath();
        string? FindStreamlineDllPath();
        string? FindDefaultIniPath();
    }

    public class DeploymentService : IDeploymentService
    {
        public const string BackupFolderName = "AetherDLLBackup";
        public const string ManifestFileName = "aether_manifest.json";

        public string? FindDxgiDllPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates = {
                @"G:\Antigravity Projects\AetherPulse-v1.2.0\src\NativeCore\build\Release\dxgi.dll",
                Path.Combine(baseDir, "Redist", "dxgi.dll")
            };
            foreach (var p in candidates) if (File.Exists(p)) return p;
            return null;
        }

        public string? FindStreamlineDllPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates = {
                @"G:\Antigravity Projects\AetherPulse-v1.2.0\src\NativeCore\build\Release\sl.interposer.dll",
                Path.Combine(baseDir, "Redist", "sl.interposer.dll")
            };
            foreach (var p in candidates) if (File.Exists(p)) return p;
            return null;
        }

        public string? FindDefaultIniPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates = {
                Path.Combine(baseDir, "aetherpulse.ini"),
                @"G:\Antigravity Projects\AetherPulse-v1.2.0\aetherpulse.ini"
            };
            foreach (var p in candidates) if (File.Exists(p)) return p;
            return null;
        }

        public bool IsDeployed(string executablePath, DeploymentMode mode)
        {
            if (string.IsNullOrWhiteSpace(executablePath)) return false;
            string targetDir = Path.GetDirectoryName(executablePath) ?? "";
            if (!Directory.Exists(targetDir)) return false;

            return File.Exists(Path.Combine(targetDir, "dxgi.dll"));
        }

        public async Task<DeploymentResult> DeployAsync(string executablePath, DeploymentMode mode, string? customIniPath = null)
        {
            return await Task.Run(() =>
            {
                var result = new DeploymentResult();
                try
                {
                    string targetDir = Path.GetDirectoryName(executablePath) ?? "";
                    if (!Directory.Exists(targetDir))
                    {
                        result.Status = DeploymentStatus.Failed;
                        result.Message = "Target directory does not exist.";
                        return result;
                    }

                    string checkBackupDir = Path.Combine(targetDir, BackupFolderName);
                    string manifestPath = Path.Combine(checkBackupDir, ManifestFileName);

                    // Atomic safety check: If backup folder or manifest already exists, prevent re-deployment overwrite
                    if (Directory.Exists(checkBackupDir) && (File.Exists(manifestPath) || File.Exists(Path.Combine(checkBackupDir, "dxgi.dll"))))
                    {
                        result.Status = DeploymentStatus.Failed;
                        result.Message = "Original files already backed up. Deployment halted to prevent overwriting original binaries.";
                        return result;
                    }

                    string? dxgiSrc = FindDxgiDllPath();
                    string? slSrc   = FindStreamlineDllPath();
                    string? iniSrc  = customIniPath ?? FindDefaultIniPath();

                    string backupDir = Path.Combine(targetDir, BackupFolderName);
                    Directory.CreateDirectory(backupDir);

                    var manifest = new BackupManifest
                    {
                        InstalledAt = DateTime.UtcNow
                    };

                    // 1. Deploy dxgi.dll (Always)
                    if (dxgiSrc != null && File.Exists(dxgiSrc))
                    {
                        string destDxgi = Path.Combine(targetDir, "dxgi.dll");
                        string backupDxgi = Path.Combine(backupDir, "dxgi.dll");
                        if (File.Exists(destDxgi) && !File.Exists(backupDxgi))
                        {
                            File.Copy(destDxgi, backupDxgi, true);
                        }
                        File.Copy(dxgiSrc, destDxgi, true);
                        result.DeployedFiles.Add("dxgi.dll");
                        manifest.InjectedFiles.Add("dxgi.dll");
                    }

// Legacy interposer handling decoupled

                    // 3. Deploy INI Config (Always)
                    if (iniSrc != null && File.Exists(iniSrc))
                    {
                        string destIni = Path.Combine(targetDir, "aetherpulse.ini");
                        File.Copy(iniSrc, destIni, true);
                        result.DeployedFiles.Add("aetherpulse.ini");
                        manifest.InjectedFiles.Add("aetherpulse.ini");
                    }

                    string json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(Path.Combine(backupDir, ManifestFileName), json);

                    result.Status = DeploymentStatus.Success;
                    result.Message = $"Successfully deployed AetherPulse Pacing Hook ({result.DeployedFiles.Count} components: dxgi.dll + ini).";
                    return result;
                }
                catch (Exception ex)
                {
                    result.Status = DeploymentStatus.Failed;
                    result.Message = ex.Message;
                    return result;
                }
            });
        }

        public async Task<DeploymentResult> UninstallAsync(string executablePath, DeploymentMode mode)
        {
            return await Task.Run(() =>
            {
                var result = new DeploymentResult();
                try
                {
                    string targetDir = Path.GetDirectoryName(executablePath) ?? "";
                    string backupDir = Path.Combine(targetDir, BackupFolderName);
                    string manifestPath = Path.Combine(backupDir, ManifestFileName);

                    if (File.Exists(manifestPath))
                    {
                        var manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(manifestPath));
                        if (manifest != null)
                        {
                            foreach (var file in manifest.InjectedFiles)
                            {
                                if (string.Equals(file, "sl.interposer.dll", StringComparison.OrdinalIgnoreCase)) continue;
                                string targetFile = Path.Combine(targetDir, file);
                                if (File.Exists(targetFile)) File.Delete(targetFile);

                                string backupFile = Path.Combine(backupDir, file);
                                if (File.Exists(backupFile))
                                {
                                    File.Copy(backupFile, targetFile, true);
                                }
                            }
                        }
                        Directory.Delete(backupDir, true);
                    }
                    else
                    {
                        // Fallback manual cleanup
                        string[] toClean = { "dxgi.dll", "aetherpulse.ini" };
                        foreach (var f in toClean)
                        {
                            string p = Path.Combine(targetDir, f);
                            if (File.Exists(p)) File.Delete(p);
                        }
                    }

                    result.Status = DeploymentStatus.Success;
                    result.Message = "Full deployment removed and original files restored.";
                    return result;
                }
                catch (Exception ex)
                {
                    result.Status = DeploymentStatus.Failed;
                    result.Message = ex.Message;
                    return result;
                }
            });
        }
    }
}



