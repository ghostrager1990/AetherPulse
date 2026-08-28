using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Principal;
using System.Threading.Tasks;
using AppUI.Models;

namespace AppUI.Services
{
    public class DeploymentService : IDeploymentService
    {
        public const string TargetDllName = "dxcore.dll";
        public const string NativeDllName = "dxcore.dll";
        public const string VersionProxyName = "version.dll";
        public const string DxgiProxyName = "dxgi.dll";
        public const string IniConfigName = "aetherpulse.ini";

        private static readonly HashSet<string> ProtectedFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            "sl.interposer.dll",
            "sl.common.dll",
            "sl.dlss.dll",
            "sl.dlss_d.dll",
            "sl.dlss_g.dll",
            "sl.reflex.dll",
            "dxgi.dll",
            "ReShade.ini",
            "ReShadePreset.ini"
        };

        public string? FindNativeCoreDllPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            string[] candidatePaths = new[]
            {
                Path.Combine(baseDir, TargetDllName),
                Path.Combine(baseDir, "Redist", TargetDllName),
                Path.Combine(baseDir, "..", "..", "..", "..", "src", "NativeCore", TargetDllName),
                Path.Combine(baseDir, "..", "..", "..", "..", "src", "AppUI", "Redist", TargetDllName),
                @"G:\Antigravity Projects\AetherPulse\src\NativeCore\dxcore.dll",
                @"G:\Antigravity Projects\AetherPulse\src\AppUI\Redist\dxcore.dll",
                @"G:\Antigravity Projects\AetherPulse\src\NativeCore\version.dll",
                @"G:\Antigravity Projects\AetherPulse\src\AppUI\Redist\AetherPulseCore.dll"
            };

            foreach (var path in candidatePaths)
            {
                try
                {
                    string fullPath = Path.GetFullPath(path);
                    if (File.Exists(fullPath)) return fullPath;
                }
                catch { }
            }
            return null;
        }

        public string? FindPayloadDirectory()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            string[] candidateDirs = new[]
            {
                Path.Combine(baseDir, "Assets", "Payload"),
                Path.Combine(baseDir, "Payload"),
                Path.Combine(baseDir, "..", "..", "..", "..", "src", "AppUI", "Assets", "Payload"),
                @"G:\Antigravity Projects\AetherPulse\src\AppUI\Assets\Payload"
            };

            foreach (var dir in candidateDirs)
            {
                try
                {
                    string fullPath = Path.GetFullPath(dir);
                    if (Directory.Exists(fullPath)) return fullPath;
                }
                catch { }
            }
            return null;
        }

        public string? FindDefaultIniPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            string[] candidatePaths = new[]
            {
                Path.Combine(baseDir, IniConfigName),
                Path.Combine(baseDir, "..", "..", "..", "..", IniConfigName),
                @"G:\Antigravity Projects\AetherPulse\aetherpulse.ini"
            };

            foreach (var path in candidatePaths)
            {
                try
                {
                    string fullPath = Path.GetFullPath(path);
                    if (File.Exists(fullPath)) return fullPath;
                }
                catch { }
            }
            return null;
        }

        public async Task<DeploymentResult> DeployAsync(string targetGameDirectory, DeploymentMode mode, string? customIniPath = null)
        {
            return await Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(targetGameDirectory) || !Directory.Exists(targetGameDirectory))
                {
                    return new DeploymentResult
                    {
                        Status = DeploymentStatus.TargetNotFound,
                        Message = $"Target directory does not exist: {targetGameDirectory}"
                    };
                }

                string? sourceDllPath = FindNativeCoreDllPath();
                if (string.IsNullOrEmpty(sourceDllPath) || !File.Exists(sourceDllPath))
                {
                    return new DeploymentResult
                    {
                        Status = DeploymentStatus.Failed,
                        Message = $"Native {NativeDllName} binary could not be found."
                    };
                }

                string? payloadDir = FindPayloadDirectory();
                string? sourceIniPath = customIniPath ?? FindDefaultIniPath();

                var deployedFiles = new List<string>();
                var backupFiles = new List<string>();

                try
                {
                    // 1. Purge all spoofing, fake NVAPI, and OptiScaler rogue files
                    string[] rogueFiles = new[]
                    {
                        "fakenvapi.dll",
                        "fakenvapi.ini",
                        "nvapi64.dll",
                        "OptiScaler.dll",
                        "OptiScaler.ini",
                        "OptiScaler.log",
                        "_nvngx.dll",
                        "nvngx.dll",
                        "d3d12.dll",
                        "dlssg_to_fsr3_amd_is_better.dll"
                    };

                    foreach (var rogue in rogueFiles)
                    {
                        string roguePath = Path.Combine(targetGameDirectory, rogue);
                        if (File.Exists(roguePath))
                        {
                            try { File.Delete(roguePath); } catch { }
                        }
                    }

                    // Restore any backups in root or streamline/
                    string[] backedUpFiles = new[] { "sl.dlss_g.dll", "sl.dlss_d.dll" };
                    foreach (var baseName in backedUpFiles)
                    {
                        string rootBak = Path.Combine(targetGameDirectory, baseName + ".bak");
                        string rootOrig = Path.Combine(targetGameDirectory, baseName);
                        if (File.Exists(rootBak))
                        {
                            if (File.Exists(rootOrig)) File.Delete(rootOrig);
                            File.Move(rootBak, rootOrig);
                        }

                        string slDir = Path.Combine(targetGameDirectory, "streamline");
                        if (Directory.Exists(slDir))
                        {
                            string slBak = Path.Combine(slDir, baseName + ".bak");
                            string slOrig = Path.Combine(slDir, baseName);
                            if (File.Exists(slBak))
                            {
                                if (File.Exists(slOrig)) File.Delete(slOrig);
                                File.Move(slBak, slOrig);
                            }
                        }
                    }

                    // Restore ReShade64.dll to dxgi.dll if present
                    string destDxgi = Path.Combine(targetGameDirectory, DxgiProxyName);
                    string destReshade = Path.Combine(targetGameDirectory, "ReShade64.dll");
                    if (File.Exists(destReshade))
                    {
                        if (File.Exists(destDxgi)) File.Delete(destDxgi);
                        File.Move(destReshade, destDxgi);
                    }

                    // 2. Deploy dxcore.dll (AetherPulse NativeCore DX12 Proxy) and clean old version.dll
                    string oldVersionPath = Path.Combine(targetGameDirectory, VersionProxyName);
                    if (File.Exists(oldVersionPath))
                    {
                        try { File.Delete(oldVersionPath); } catch { }
                    }

                    string destDxcore = Path.Combine(targetGameDirectory, TargetDllName);
                    DeployFileWithBackup(sourceDllPath, destDxcore, backupFiles, deployedFiles);

                    // 3. Deploy standard AMD FidelityFX libraries
                    if (!string.IsNullOrEmpty(payloadDir) && Directory.Exists(payloadDir))
                    {
                        string[] fidelityFxDlls = new[]
                        {
                            "amd_fidelityfx_dx12.dll",
                            "amd_fidelityfx_denoiser_dx12.dll"
                        };

                        foreach (var dll in fidelityFxDlls)
                        {
                            string srcDll = Path.Combine(payloadDir, dll);
                            if (!File.Exists(srcDll)) srcDll = Path.Combine(payloadDir, "OptiScaler", dll);

                            if (File.Exists(srcDll))
                            {
                                string destDll = Path.Combine(targetGameDirectory, dll);
                                DeployFileWithBackup(srcDll, destDll, backupFiles, deployedFiles);
                            }
                        }
                    }

                    // 4. Deploy AetherPulse ini config
                    if (!string.IsNullOrEmpty(sourceIniPath) && File.Exists(sourceIniPath))
                    {
                        string destIni = Path.Combine(targetGameDirectory, IniConfigName);
                        File.Copy(sourceIniPath, destIni, true);
                        deployedFiles.Add(destIni);
                    }

                    return new DeploymentResult
                    {
                        Status = DeploymentStatus.Success,
                        Message = $"Successfully deployed Pure NativeCore (dxcore.dll D3D12 Frame Pacer) to {targetGameDirectory}.",
                        DeployedFiles = deployedFiles,
                        BackupFiles = backupFiles
                    };
                }
                catch (UnauthorizedAccessException ex)
                {
                    bool isElevated = IsRunningAsAdministrator();
                    return new DeploymentResult
                    {
                        Status = isElevated ? DeploymentStatus.AccessDenied : DeploymentStatus.ElevationRequired,
                        Message = isElevated
                            ? $"Access denied to {targetGameDirectory}: {ex.Message}"
                            : "Administrator permissions required to deploy to this directory.",
                        DeployedFiles = deployedFiles,
                        BackupFiles = backupFiles
                    };
                }
                catch (IOException ex)
                {
                    return new DeploymentResult
                    {
                        Status = DeploymentStatus.FileLocked,
                        Message = $"A file in the target directory is locked: {ex.Message}",
                        DeployedFiles = deployedFiles,
                        BackupFiles = backupFiles
                    };
                }
                catch (Exception ex)
                {
                    return new DeploymentResult
                    {
                        Status = DeploymentStatus.Failed,
                        Message = $"Deployment failed: {ex.Message}",
                        DeployedFiles = deployedFiles,
                        BackupFiles = backupFiles
                    };
                }
            });
        }

        public async Task<DeploymentResult> UninstallAsync(string targetGameDirectory, DeploymentMode mode)
        {
            return await Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(targetGameDirectory) || !Directory.Exists(targetGameDirectory))
                {
                    return new DeploymentResult
                    {
                        Status = DeploymentStatus.TargetNotFound,
                        Message = $"Target directory does not exist: {targetGameDirectory}"
                    };
                }

                var removedFiles = new List<string>();

                try
                {
                    string[] removableFiles = new[]
                    {
                        TargetDllName,
                        VersionProxyName,
                        "fakenvapi.dll",
                        "fakenvapi.ini",
                        "nvapi64.dll",
                        "OptiScaler.dll",
                        "OptiScaler.ini",
                        "OptiScaler.log",
                        "dlssg_to_fsr3_amd_is_better.dll",
                        "d3d12.dll",
                        "nvngx.dll",
                        "_nvngx.dll",
                        IniConfigName,
                        "AetherPulseCore.dll"
                    };

                    foreach (var name in removableFiles)
                    {
                        if (ProtectedFiles.Contains(name)) continue;

                        string target = Path.Combine(targetGameDirectory, name);
                        RevertOrDeleteFile(target, removedFiles);
                    }

                    return new DeploymentResult
                    {
                        Status = DeploymentStatus.Success,
                        Message = $"Successfully uninstalled AetherPulse from {targetGameDirectory}.",
                        DeployedFiles = removedFiles
                    };
                }
                catch (Exception ex)
                {
                    return new DeploymentResult
                    {
                        Status = DeploymentStatus.Failed,
                        Message = $"Uninstallation failed: {ex.Message}"
                    };
                }
            });
        }

        public bool IsDeployed(string targetGameDirectory, DeploymentMode mode)
        {
            if (string.IsNullOrWhiteSpace(targetGameDirectory) || !Directory.Exists(targetGameDirectory))
            {
                return false;
            }

            return File.Exists(Path.Combine(targetGameDirectory, TargetDllName)) || File.Exists(Path.Combine(targetGameDirectory, VersionProxyName));
        }

        private static void DeployFileWithBackup(string sourcePath, string destPath, List<string> backupList, List<string> deployedList)
        {
            string fileName = Path.GetFileName(destPath);
            if (ProtectedFiles.Contains(fileName)) return;

            if (File.Exists(destPath))
            {
                string backupPath = destPath + ".bak";
                if (!File.Exists(backupPath))
                {
                    File.Copy(destPath, backupPath, true);
                    backupList.Add(backupPath);
                }
            }

            File.Copy(sourcePath, destPath, true);
            deployedList.Add(destPath);
        }

        private static void RevertOrDeleteFile(string targetPath, List<string> removedList)
        {
            string fileName = Path.GetFileName(targetPath);
            if (ProtectedFiles.Contains(fileName)) return;

            string backupPath = targetPath + ".bak";

            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
                removedList.Add(targetPath);
            }

            if (File.Exists(backupPath))
            {
                File.Move(backupPath, targetPath);
            }
        }

        private static bool IsRunningAsAdministrator()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
