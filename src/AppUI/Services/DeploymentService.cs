using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading.Tasks;
using AppUI.Models;

namespace AppUI.Services
{
    public class DeploymentService : IDeploymentService
    {
        public const string NativeDllName = "AetherPulseCore.dll";
        public const string DxgiProxyName = "dxgi.dll";
        public const string DxgiChainName = "dxgi_chain.dll";
        public const string StreamlineInterposerName = "sl.interposer.dll";
        public const string IniConfigName = "aetherpulse.ini";

        public string? FindNativeCoreDllPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            string[] candidatePaths = new[]
            {
                Path.Combine(baseDir, NativeDllName),
                Path.Combine(baseDir, "Redist", NativeDllName),
                Path.Combine(baseDir, "NativeCore", NativeDllName),
                Path.Combine(baseDir, "..", "..", "..", "..", "src", "NativeCore", NativeDllName),
                Path.Combine(baseDir, "..", "..", "..", "..", "src", "AppUI", "Redist", NativeDllName),
                Path.Combine(baseDir, "..", "..", "..", "..", "src", "NativeCore", "build", "Release", NativeDllName),
                Path.Combine(baseDir, "..", "..", "..", "..", "build", "Release", NativeDllName),
                @"G:\Antigravity Projects\AetherPulse\src\NativeCore\AetherPulseCore.dll",
                @"G:\Antigravity Projects\AetherPulse\src\AppUI\Redist\AetherPulseCore.dll"
            };

            foreach (var path in candidatePaths)
            {
                try
                {
                    string fullPath = Path.GetFullPath(path);
                    if (File.Exists(fullPath))
                    {
                        return fullPath;
                    }
                }
                catch
                {
                }
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
                    if (File.Exists(fullPath))
                    {
                        return fullPath;
                    }
                }
                catch
                {
                }
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

                string? sourceIniPath = customIniPath ?? FindDefaultIniPath();

                var deployedFiles = new List<string>();
                var backupFiles = new List<string>();
                bool chainedForeignDxgi = false;

                try
                {
                    // Deploy DLL depending on mode
                    if (mode == DeploymentMode.DxgiProxy || mode == DeploymentMode.Both)
                    {
                        string destDxgi = Path.Combine(targetGameDirectory, DxgiProxyName);
                        string destChain = Path.Combine(targetGameDirectory, DxgiChainName);

                        // Check if a foreign dxgi.dll already exists (e.g. ReShade, RenoDX, OptiScaler)
                        if (File.Exists(destDxgi))
                        {
                            long existingSize = new FileInfo(destDxgi).Length;
                            long sourceSize = new FileInfo(sourceDllPath).Length;

                            if (existingSize != sourceSize)
                            {
                                // Rename foreign dxgi.dll to dxgi_chain.dll so AetherPulse can forward calls to it
                                if (!File.Exists(destChain))
                                {
                                    File.Move(destDxgi, destChain);
                                    backupFiles.Add(destChain);
                                }
                                else
                                {
                                    File.Copy(destDxgi, destChain, true);
                                }
                                chainedForeignDxgi = true;
                            }
                        }

                        DeployFileWithBackup(sourceDllPath, destDxgi, backupFiles, deployedFiles);
                    }

                    if (mode == DeploymentMode.StreamlineInterposer || mode == DeploymentMode.Both)
                    {
                        string destSl = Path.Combine(targetGameDirectory, StreamlineInterposerName);
                        DeployFileWithBackup(sourceDllPath, destSl, backupFiles, deployedFiles);
                    }

                    // Deploy INI config file with chaining parameters if foreign mod detected
                    if (!string.IsNullOrEmpty(sourceIniPath) && File.Exists(sourceIniPath))
                    {
                        string destIni = Path.Combine(targetGameDirectory, IniConfigName);
                        string iniContent = File.ReadAllText(sourceIniPath);

                        if (chainedForeignDxgi)
                        {
                            if (!iniContent.Contains("[Chaining]"))
                            {
                                iniContent += "\r\n\r\n[Chaining]\r\nEnableProxyChaining=true\r\nOriginalDllPath=dxgi_chain.dll\r\n";
                            }
                            else if (!iniContent.Contains("OriginalDllPath=dxgi_chain.dll"))
                            {
                                iniContent = iniContent.Replace("OriginalDllPath=", "OriginalDllPath=dxgi_chain.dll");
                            }
                        }

                        File.WriteAllText(destIni, iniContent);
                        deployedFiles.Add(destIni);
                    }

                    // Deploy packaged Redist support files (Anti-Lag 2 & Shaders)
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string[] redistDirs = new[]
                    {
                        Path.Combine(baseDir, "Redist"),
                        Path.Combine(baseDir, "..", "..", "..", "..", "src", "AppUI", "Redist"),
                        @"G:\Antigravity Projects\AetherPulse\src\AppUI\Redist"
                    };

                    foreach (var rDir in redistDirs)
                    {
                        if (Directory.Exists(rDir))
                        {
                            var supportFiles = Directory.GetFiles(rDir, "*.*");
                            foreach (var sFile in supportFiles)
                            {
                                string fName = Path.GetFileName(sFile);
                                if (!string.Equals(fName, NativeDllName, StringComparison.OrdinalIgnoreCase))
                                {
                                    string destPath = Path.Combine(targetGameDirectory, fName);
                                    DeployFileWithBackup(sFile, destPath, backupFiles, deployedFiles);
                                }
                            }
                            break;
                        }
                    }

                    string successMsg = chainedForeignDxgi
                        ? $"Successfully deployed AetherPulse with Mod Chaining (ReShade/RenoDX preserved as {DxgiChainName}) to {targetGameDirectory}."
                        : $"Successfully deployed AetherPulse ({mode}) to {targetGameDirectory}.";

                    return new DeploymentResult
                    {
                        Status = DeploymentStatus.Success,
                        Message = successMsg,
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
                        Message = $"A file in the target directory is currently locked (is the game running?): {ex.Message}",
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
                    if (mode == DeploymentMode.DxgiProxy || mode == DeploymentMode.Both)
                    {
                        string target = Path.Combine(targetGameDirectory, DxgiProxyName);
                        string chainTarget = Path.Combine(targetGameDirectory, DxgiChainName);

                        RevertOrDeleteFile(target, removedFiles);

                        // If dxgi_chain.dll exists (e.g. ReShade/RenoDX was chained), restore it to dxgi.dll
                        if (File.Exists(chainTarget) && !File.Exists(target))
                        {
                            File.Move(chainTarget, target);
                            removedFiles.Add(chainTarget + " -> restored to " + DxgiProxyName);
                        }
                    }

                    if (mode == DeploymentMode.StreamlineInterposer || mode == DeploymentMode.Both)
                    {
                        string target = Path.Combine(targetGameDirectory, StreamlineInterposerName);
                        RevertOrDeleteFile(target, removedFiles);
                    }

                    string iniTarget = Path.Combine(targetGameDirectory, IniConfigName);
                    RevertOrDeleteFile(iniTarget, removedFiles);

                    // Revert or delete packaged support files (FidelityFX Modular DLLs, Anti-Lag, Shaders)
                    string[] supportFileNames = new[]
                    {
                        "amd_fidelityfx_upscaler_dx12.dll",
                        "amd_fidelityfx_framegeneration_dx12.dll",
                        "amd_fidelityfx_denoiser_dx12.dll",
                        "amd_fidelityfx_radiancecache_dx12.dll",
                        "amd_fidelityfx_loader_dx12.dll",
                        "amd_antilag2_dx12.dll",
                        "amd_ags_x64.dll",
                        "amd_acs_x64.dll",
                        "RCAS_CS.cso"
                    };
                    foreach (var sName in supportFileNames)
                    {
                        string sTarget = Path.Combine(targetGameDirectory, sName);
                        if (File.Exists(sTarget))
                        {
                            RevertOrDeleteFile(sTarget, removedFiles);
                        }
                    }

                    return new DeploymentResult
                    {
                        Status = DeploymentStatus.Success,
                        Message = $"Successfully uninstalled AetherPulse proxy files from {targetGameDirectory}.",
                        DeployedFiles = removedFiles
                    };
                }
                catch (UnauthorizedAccessException ex)
                {
                    return new DeploymentResult
                    {
                        Status = DeploymentStatus.AccessDenied,
                        Message = $"Access denied during uninstallation: {ex.Message}"
                    };
                }
                catch (IOException ex)
                {
                    return new DeploymentResult
                    {
                        Status = DeploymentStatus.FileLocked,
                        Message = $"Cannot remove proxy files while target game is running: {ex.Message}"
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

            bool hasDxgi = File.Exists(Path.Combine(targetGameDirectory, DxgiProxyName));
            bool hasSl = File.Exists(Path.Combine(targetGameDirectory, StreamlineInterposerName));

            return mode switch
            {
                DeploymentMode.DxgiProxy => hasDxgi,
                DeploymentMode.StreamlineInterposer => hasSl,
                DeploymentMode.Both => hasDxgi && hasSl,
                _ => false
            };
        }

        private static void DeployFileWithBackup(string sourcePath, string destPath, List<string> backupList, List<string> deployedList)
        {
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
