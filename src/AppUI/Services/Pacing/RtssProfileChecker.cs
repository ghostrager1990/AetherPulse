using Microsoft.Win32;
using System;
using System.IO;

namespace AppUI.Services.Pacing
{
    public static class RtssProfileChecker
    {
        public static bool HasActiveFrameCap(string gameExecutableName)
        {
            try
            {
                string rtssPath = GetRtssInstallPath();
                if (string.IsNullOrEmpty(rtssPath)) return false;

                string profilesDir = Path.Combine(rtssPath, "Profiles");
                if (!Directory.Exists(profilesDir)) return false;

                // 1. Check Global Profile for an active frame cap (> 0)
                if (CheckProfileForFramerateLimit(Path.Combine(profilesDir, "Global")) ||
                    CheckProfileForFramerateLimit(Path.Combine(profilesDir, "Global.cfg")))
                {
                    return true;
                }

                // 2. Check Game-Specific Executable Profile if provided
                if (!string.IsNullOrEmpty(gameExecutableName))
                {
                    string profileName = gameExecutableName.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase) 
                        ? gameExecutableName 
                        : gameExecutableName + ".cfg";

                    string gameCfgPath = Path.Combine(profilesDir, profileName);
                    if (CheckProfileForFramerateLimit(gameCfgPath))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RTSS Profile Check Error] {ex.Message}");
            }

            return false;
        }

        private static string GetRtssInstallPath()
        {
            string[] registryKeys = {
                @"SOFTWARE\WOW6432Node\Unwinder\RTSS",
                @"SOFTWARE\Unwinder\RTSS"
            };

            foreach (var keyPath in registryKeys)
            {
                using var key = Registry.LocalMachine.OpenSubKey(keyPath) ?? Registry.CurrentUser.OpenSubKey(keyPath);
                object? pathObj = key?.GetValue("Path");
                if (pathObj is string path && Directory.Exists(path))
                {
                    return path;
                }
            }

            string defaultPath = @"C:\Program Files (x86)\RivaTuner Statistics Server";
            if (Directory.Exists(defaultPath)) return defaultPath;

            return string.Empty;
        }

        private static bool CheckProfileForFramerateLimit(string filePath)
        {
            if (!File.Exists(filePath)) return false;

            try
            {
                foreach (var line in File.ReadLines(filePath))
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("FramerateLimit", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = trimmed.Split('=');
                        if (parts.Length == 2 && float.TryParse(parts[1].Trim(), out float limit))
                        {
                            if (limit > 0f) return true;
                        }
                    }
                }
            }
            catch
            {
                // Handle file locks gracefully if RTSS is actively writing
            }

            return false;
        }
    }
}
