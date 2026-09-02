using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AppUI.Models;

namespace AppUI.Services
{
    public interface IDeploymentService
    {
        bool IsDeployed(string executablePath, DeploymentMode mode);
        Task<DeploymentResult> DeployAsync(string executablePath, DeploymentMode mode, string targetProxyDllName = "dxgi.dll", string? customIniPath = null);
        Task<DeploymentResult> UninstallAsync(string executablePath, DeploymentMode mode);
        string? FindPayloadDllPath();
        string? FindDefaultIniPath();
    }

    public class DeploymentService : IDeploymentService
    {
        private static string? ResolveCandidatePath(params string[] relativePaths)
        {
            string baseDir = AppContext.BaseDirectory;
            foreach (var rel in relativePaths)
            {
                string full = Path.GetFullPath(Path.Combine(baseDir, rel));
                if (File.Exists(full)) return full;
            }
            return null;
        }

        public string? FindPayloadDllPath() =>
            ResolveCandidatePath(
                Path.Combine("Redist", "dxgi.dll"),
                "dxgi.dll",
                Path.Combine("..", "..", "..", "..", "NativeCore", "build", "Release", "dxgi.dll"));

        public string? FindDefaultIniPath() =>
            ResolveCandidatePath(
                "aetherpulse.ini",
                Path.Combine("..", "..", "..", "..", "aetherpulse.ini"));

        public bool IsDeployed(string executablePath, DeploymentMode mode)
        {
            if (string.IsNullOrWhiteSpace(executablePath)) return false;
            string targetDir = GameBackupService.ResolveGameDirectory(executablePath);
            if (!Directory.Exists(targetDir)) return false;

            // 1. Check if an active backup manifest exists
            if (GameBackupService.HasExistingBackup(targetDir))
            {
                var manifest = GameBackupService.LoadManifest(targetDir);
                if (manifest != null && manifest.InjectedFiles.Count > 0)
                {
                    return true;
                }
            }

            // 2. Fallback check for common proxy entry points
            return File.Exists(Path.Combine(targetDir, "dxgi.dll")) ||
                   File.Exists(Path.Combine(targetDir, "version.dll")) ||
                   File.Exists(Path.Combine(targetDir, "d3d12.dll"));
        }

        public async Task<DeploymentResult> DeployAsync(string executablePath, DeploymentMode mode, string targetProxyDllName = "dxgi.dll", string? customIniPath = null)
        {
            return await Task.Run(() =>
            {
                var result = new DeploymentResult();
                try
                {
                    string targetDir = GameBackupService.ResolveGameDirectory(executablePath);
                    if (!Directory.Exists(targetDir))
                    {
                        result.Status = DeploymentStatus.Failed;
                        result.Message = "Target game directory does not exist.";
                        return result;
                    }

                    if (GameBackupService.HasExistingBackup(targetDir))
                    {
                        result.Status = DeploymentStatus.Failed;
                        result.Message = "Original files are already backed up. Please uninstall existing deployment first.";
                        return result;
                    }

                    string? payloadDllSrc = FindPayloadDllPath();
                    if (string.IsNullOrEmpty(payloadDllSrc) || !File.Exists(payloadDllSrc))
                    {
                        result.Status = DeploymentStatus.Failed;
                        result.Message = "AetherPulse core hook binary could not be found.";
                        return result;
                    }

                    string? iniSrc = customIniPath ?? FindDefaultIniPath();

                    // Prepare files to inject: Selected proxy name (e.g. dxgi.dll or version.dll) + INI
                    var filesToDeploy = new Dictionary<string, string>
                    {
                        { targetProxyDllName, payloadDllSrc }
                    };

                    if (!string.IsNullOrEmpty(iniSrc) && File.Exists(iniSrc))
                    {
                        filesToDeploy["aetherpulse.ini"] = iniSrc;
                    }

                    GameBackupService.DeployAndBackup(targetDir, filesToDeploy);
                    result.DeployedFiles.AddRange(filesToDeploy.Keys);

                    result.Status = DeploymentStatus.Success;
                    result.Message = $"Successfully deployed AetherPulse as {targetProxyDllName} ({result.DeployedFiles.Count} components).";
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
                    string targetDir = GameBackupService.ResolveGameDirectory(executablePath);
                    if (!Directory.Exists(targetDir))
                    {
                        result.Status = DeploymentStatus.Failed;
                        result.Message = "Target game directory does not exist.";
                        return result;
                    }

                    // Delegate complete rollback and artifact cleanup
                    GameBackupService.UninstallAndRestore(targetDir);

                    result.Status = DeploymentStatus.Success;
                    result.Message = "Deployment removed and original files restored.";
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