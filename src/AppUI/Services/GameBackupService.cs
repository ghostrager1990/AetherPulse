using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using AppUI.Models;

namespace AppUI.Services
{
    public static class GameBackupService
    {
        public const string BackupDirName = "AetherDLLBackup";
        public const string ManifestName = "aether_manifest.json";

        public static string ResolveGameDirectory(string targetPath)
        {
            if (string.IsNullOrWhiteSpace(targetPath)) return string.Empty;

            if (File.Exists(targetPath) || targetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetDirectoryName(targetPath) ?? string.Empty;
            }

            return targetPath;
        }

        public static bool HasExistingBackup(string gameDir)
        {
            string backupPath = Path.Combine(gameDir, BackupDirName);
            string manifestPath = Path.Combine(backupPath, ManifestName);
            return Directory.Exists(backupPath) && File.Exists(manifestPath);
        }

        public static BackupManifest? LoadManifest(string gameDir)
        {
            string manifestPath = Path.Combine(gameDir, BackupDirName, ManifestName);
            if (!File.Exists(manifestPath)) return null;

            try
            {
                string json = File.ReadAllText(manifestPath);
                return JsonSerializer.Deserialize<BackupManifest>(json);
            }
            catch
            {
                return null;
            }
        }

        public static void DeployAndBackup(string targetPath, Dictionary<string, string> packageFilesToDeploy)
        {
            string gameDir = ResolveGameDirectory(targetPath);
            if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
            {
                throw new DirectoryNotFoundException($"Target directory does not exist: {gameDir}");
            }

            string backupPath = Path.Combine(gameDir, BackupDirName);
            string manifestPath = Path.Combine(backupPath, ManifestName);

            if (Directory.Exists(backupPath) && File.Exists(manifestPath))
            {
                throw new InvalidOperationException("Active backup manifest already exists. Re-deployment halted to protect original binaries.");
            }

            Directory.CreateDirectory(backupPath);

            var manifest = new BackupManifest
            {
                InstalledAt = DateTime.UtcNow
            };

            foreach (var (targetFileName, sourcePackagePath) in packageFilesToDeploy)
            {
                string targetGameFile = Path.Combine(gameDir, targetFileName);

                // If original file exists, back it up atomically with SHA-256 validation
                if (File.Exists(targetGameFile))
                {
                    string primaryBackup = Path.Combine(backupPath, targetFileName);
                    File.Copy(targetGameFile, primaryBackup, overwrite: true);
                    manifest.OriginalFileHashes[targetFileName] = ComputeSha256(primaryBackup);
                }

                File.Copy(sourcePackagePath, targetGameFile, overwrite: true);
                manifest.InjectedFiles.Add(targetFileName);
            }

            string manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(manifestPath, manifestJson);
        }

        public static void UninstallAndRestore(string targetPath)
        {
            string gameDir = ResolveGameDirectory(targetPath);
            if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir)) return;

            string backupPath = Path.Combine(gameDir, BackupDirName);
            string manifestPath = Path.Combine(backupPath, ManifestName);

            if (!Directory.Exists(backupPath) || !File.Exists(manifestPath))
            {
                string[] defaultInjections = { "dxgi.dll", "version.dll", "d3d12.dll", "aetherpulse.ini", "aetherpulse_debug.log" };
                foreach (var file in defaultInjections)
                {
                    string p = Path.Combine(gameDir, file);
                    try { if (File.Exists(p)) File.Delete(p); } catch { }
                }
                return;
            }

            var manifest = LoadManifest(gameDir);
            if (manifest == null) return;

            foreach (var injected in manifest.InjectedFiles)
            {
                string filePath = Path.Combine(gameDir, injected);
                try { if (File.Exists(filePath)) File.Delete(filePath); } catch { }
            }

            foreach (var (origFile, expectedHash) in manifest.OriginalFileHashes)
            {
                string backupFile = Path.Combine(backupPath, origFile);
                string restoreTarget = Path.Combine(gameDir, origFile);

                if (File.Exists(backupFile) && ComputeSha256(backupFile) == expectedHash)
                {
                    File.Move(backupFile, restoreTarget, overwrite: true);
                }
            }

            try
            {
                Directory.Delete(backupPath, recursive: true);
            }
            catch { }
        }

        public static void CleanGameDirectory(string targetPath) => UninstallAndRestore(targetPath);

        private static string ComputeSha256(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
    }
}