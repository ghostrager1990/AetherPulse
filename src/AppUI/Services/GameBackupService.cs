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

        public static void CleanGameDirectory(string targetPath)
        {
            string gameDir = File.Exists(targetPath) && targetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(targetPath)!
                : targetPath;

            if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir)) return;

            // 1. Restore any backed up original files
            UninstallAndRestore(gameDir);

            // 2. Remove all legacy proxy DLLs and configs to guarantee vanilla state
            string[] proxyFiles = new[]
            {
                "dxgi.dll",
                "version.dll",
                "dxcore.dll",
                "aetherpulse.ini",
                "aetherpulse_debug.log"
            };

            foreach (var file in proxyFiles)
            {
                string fullPath = Path.Combine(gameDir, file);
                try
                {
                    if (File.Exists(fullPath)) File.Delete(fullPath);
                }
                catch { }
            }
        }

        public static void DeployAndBackup(string targetPath, Dictionary<string, string> packageFilesToDeploy)
        {
            string gameDir = File.Exists(targetPath) && targetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(targetPath)!
                : targetPath;

            string backupPath = Path.Combine(gameDir, BackupDirName);
            Directory.CreateDirectory(backupPath);

            var manifest = new BackupManifest();

            foreach (var (fileName, sourcePackagePath) in packageFilesToDeploy)
            {
                string targetGameFile = Path.Combine(gameDir, fileName);

                if (File.Exists(targetGameFile))
                {
                    string destBackup = Path.Combine(backupPath, fileName);
                    string destBakCopy = Path.Combine(backupPath, $"{fileName}.bak");

                    File.Copy(targetGameFile, destBackup, overwrite: true);
                    File.Copy(targetGameFile, destBakCopy, overwrite: true);

                    manifest.OriginalFileHashes[fileName] = ComputeSha256(destBackup);
                }

                File.Copy(sourcePackagePath, targetGameFile, overwrite: true);
                manifest.InjectedFiles.Add(fileName);
            }

            string manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(backupPath, ManifestName), manifestJson);
        }

        public static void UninstallAndRestore(string targetPath)
        {
            string gameDir = File.Exists(targetPath) && targetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(targetPath)!
                : targetPath;

            string backupPath = Path.Combine(gameDir, BackupDirName);
            string manifestPath = Path.Combine(backupPath, ManifestName);

            if (!Directory.Exists(backupPath) || !File.Exists(manifestPath))
                return;

            var manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(manifestPath));
            if (manifest == null) return;

            foreach (var injected in manifest.InjectedFiles)
            {
                string filePath = Path.Combine(gameDir, injected);
                if (File.Exists(filePath)) File.Delete(filePath);
            }

            string iniPath = Path.Combine(gameDir, "aetherpulse.ini");
            if (File.Exists(iniPath)) File.Delete(iniPath);

            foreach (var (origFile, expectedHash) in manifest.OriginalFileHashes)
            {
                string primaryBackup = Path.Combine(backupPath, origFile);
                string bakBackup = Path.Combine(backupPath, $"{origFile}.bak");
                string restoreTarget = Path.Combine(gameDir, origFile);

                if (File.Exists(primaryBackup) && ComputeSha256(primaryBackup) == expectedHash)
                {
                    File.Move(primaryBackup, restoreTarget, overwrite: true);
                }
                else if (File.Exists(bakBackup))
                {
                    File.Move(bakBackup, restoreTarget, overwrite: true);
                }
            }

            Directory.Delete(backupPath, recursive: true);
        }

        private static string ComputeSha256(string filePath)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            return Convert.ToHexString(sha.ComputeHash(stream));
        }
    }
}
