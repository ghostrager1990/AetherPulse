using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AppUI.Services
{
    public class GameValidationResult
    {
        public bool IsDx12Supported { get; set; }
        public bool HasDlssOrStreamline { get; set; }
        public bool IsFullyCompatible => IsDx12Supported && HasDlssOrStreamline;
        public string FailureReason { get; set; } = string.Empty;
        public string DetectedDlssFile { get; set; } = string.Empty;
        public string DetectedDx12Reason { get; set; } = string.Empty;
    }

    public static class GameValidationService
    {
        private static readonly string[] StreamlineDlls = new[]
        {
            "nvngx_dlss.dll",
            "nvngx_dlss_d.dll",
            "sl.interposer.dll",
            "sl.dlss.dll",
            "sl.dlss_d.dll",
            "sl.common.dll",
            "nvngx.dll",
            "sl.nis.dll",
            "sl.reflex.dll"
        };

        private static readonly HashSet<string> StreamlineDllSet = new(StreamlineDlls, StringComparer.OrdinalIgnoreCase);

        private static readonly byte[] D3D12DllBytes = Encoding.ASCII.GetBytes("d3d12.dll");
        private static readonly byte[] D3D12CreateBytes = Encoding.ASCII.GetBytes("D3D12CreateDevice");
        private static readonly byte[] Vulkan1DllBytes = Encoding.ASCII.GetBytes("vulkan-1.dll");
        private static readonly byte[] VkCreateInstanceBytes = Encoding.ASCII.GetBytes("vkCreateInstance");

        private static readonly EnumerationOptions SafeEnumOptions = new()
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            MaxRecursionDepth = 3,
            ReturnSpecialDirectories = false
        };

        public static GameValidationResult ValidateGameExecutable(string exePath)
        {
            var result = new GameValidationResult();

            try
            {
                if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                {
                    result.FailureReason = "Executable file not found or path is invalid.";
                    return result;
                }

                string exeDir = Path.GetDirectoryName(exePath) ?? string.Empty;

                // 1. Check Direct3D 12 support
                result.IsDx12Supported = CheckDirect3D12Support(exePath, exeDir, out string dx12Reason);
                result.DetectedDx12Reason = dx12Reason;

                // 2. Check DLSS / Streamline integration
                result.HasDlssOrStreamline = CheckDlssOrStreamline(exeDir, out string dlssFile);
                result.DetectedDlssFile = dlssFile;

                if (!result.IsDx12Supported && !result.HasDlssOrStreamline)
                {
                    result.FailureReason = "Game uses legacy DirectX/API and no DLSS/Streamline runtime DLLs were detected.";
                }
                else if (!result.IsDx12Supported)
                {
                    result.FailureReason = "Game does not appear to use DirectX 12 (D3D12). AetherPulse requires D3D12 for swapchain pacing and wavelet denoising.";
                }
                else if (!result.HasDlssOrStreamline)
                {
                    result.FailureReason = "No DLSS / Streamline runtime DLLs (nvngx_dlss.dll / sl.interposer.dll) were found in the game directory.";
                }
            }
            catch (Exception ex)
            {
                // Fallback gracefully rather than crashing
                result.IsDx12Supported = true;
                result.HasDlssOrStreamline = false;
                result.FailureReason = $"Diagnostic scan encountered non-critical error: {ex.Message}";
            }

            return result;
        }

        private static bool CheckDirect3D12Support(string exePath, string exeDir, out string reason)
        {
            reason = "No Direct3D 12 imports or runtime dependencies found.";

            try
            {
                var fileInfo = new FileInfo(exePath);
                if (fileInfo.Length >= 512)
                {
                    // Method A: Safe PE header inspection
                    using var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (fs.Length >= 0x40)
                    {
                        using var reader = new BinaryReader(fs);
                        ushort dosMagic = reader.ReadUInt16(); // 'MZ' (0x5A4D)
                        if (dosMagic == 0x5A4D)
                        {
                            fs.Seek(0x3C, SeekOrigin.Begin);
                            int peOffset = reader.ReadInt32();

                            if (peOffset > 0 && peOffset + 4 < fs.Length)
                            {
                                fs.Seek(peOffset, SeekOrigin.Begin);
                                uint peSignature = reader.ReadUInt32();
                                if (peSignature == 0x00004550) // "PE\0\0"
                                {
                                    // Valid PE executable, proceed to scan first 4MB for D3D12 import symbols
                                    int readSize = (int)Math.Min(fs.Length, 4 * 1024 * 1024);
                                    byte[] buffer = new byte[readSize];
                                    fs.Seek(0, SeekOrigin.Begin);
                                    int bytesRead = fs.Read(buffer, 0, buffer.Length);
                                    ReadOnlySpan<byte> span = new ReadOnlySpan<byte>(buffer, 0, bytesRead);

                                    if (span.IndexOf(D3D12DllBytes) >= 0 || span.IndexOf(D3D12CreateBytes) >= 0)
                                    {
                                        reason = "DirectX 12 (d3d12.dll) import detected in executable PE headers.";
                                        return true;
                                    }

                                    if (span.IndexOf(Vulkan1DllBytes) >= 0 || span.IndexOf(VkCreateInstanceBytes) >= 0)
                                    {
                                        reason = "Vulkan API detected.";
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            // Method B: Search Game Directory and immediate subfolders with safe enumeration
            try
            {
                if (!string.IsNullOrEmpty(exeDir) && Directory.Exists(exeDir))
                {
                    var d3d12Files = Directory.EnumerateFiles(exeDir, "*d3d12*.dll", SafeEnumOptions).Take(1).ToList();
                    if (d3d12Files.Count > 0)
                    {
                        reason = $"Direct3D 12 runtime component ({Path.GetFileName(d3d12Files[0])}) found in directory.";
                        return true;
                    }

                    // Check immediate parent directory if this is a nested Binaries/Win64 or bin folder
                    string? parentDir = Directory.GetParent(exeDir)?.FullName;
                    if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
                    {
                        string dirName = Path.GetFileName(exeDir).ToLowerInvariant();
                        if (dirName == "win64" || dirName == "binaries" || dirName == "bin" || dirName == "x64")
                        {
                            var parentD3D12 = Directory.EnumerateFiles(parentDir, "*d3d12*.dll", SafeEnumOptions).Take(1).ToList();
                            if (parentD3D12.Count > 0)
                            {
                                reason = $"Direct3D 12 runtime found in adjacent folder ({Path.GetFileName(parentD3D12[0])}).";
                                return true;
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool CheckDlssOrStreamline(string exeDir, out string detectedFile)
        {
            detectedFile = string.Empty;

            if (string.IsNullOrEmpty(exeDir) || !Directory.Exists(exeDir))
            {
                return false;
            }

            try
            {
                // Fast search in immediate exe directory
                foreach (var dllName in StreamlineDlls)
                {
                    string target = Path.Combine(exeDir, dllName);
                    if (File.Exists(target))
                    {
                        detectedFile = dllName;
                        return true;
                    }
                }

                // Safe recursive search up to depth 3
                var found = Directory.EnumerateFiles(exeDir, "*.dll", SafeEnumOptions)
                    .FirstOrDefault(f => StreamlineDllSet.Contains(Path.GetFileName(f)));

                if (!string.IsNullOrEmpty(found))
                {
                    detectedFile = Path.GetFileName(found);
                    return true;
                }

                // Check parent directory if inside a subfolder
                string? parentDir = Directory.GetParent(exeDir)?.FullName;
                if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
                {
                    string dirName = Path.GetFileName(exeDir).ToLowerInvariant();
                    if (dirName == "win64" || dirName == "binaries" || dirName == "bin" || dirName == "x64")
                    {
                        var parentFound = Directory.EnumerateFiles(parentDir, "*.dll", SafeEnumOptions)
                            .FirstOrDefault(f => StreamlineDllSet.Contains(Path.GetFileName(f)));

                        if (!string.IsNullOrEmpty(parentFound))
                        {
                            detectedFile = Path.GetFileName(parentFound);
                            return true;
                        }
                    }
                }
            }
            catch
            {
            }

            return false;
        }
    }
}
