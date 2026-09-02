using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AppUI.Services
{
    public enum RiskSeverity
    {
        Low,
        Moderate,
        Critical
    }

    public class AntiCheatCheckResult
    {
        public bool IsOnlineOrProtectedGame { get; set; }
        public string DetectedSystem { get; set; } = string.Empty;
        public RiskSeverity Level { get; set; } = RiskSeverity.Low;
        public string Details { get; set; } = string.Empty;
    }

    public static class AntiCheatDetectionService
    {
        private static readonly EnumerationOptions SafeEnumOptions = new()
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            MaxRecursionDepth = 2,
            ReturnSpecialDirectories = false
        };

        private static readonly Dictionary<string, (string SystemName, RiskSeverity Level)> AntiCheatFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            // Easy Anti-Cheat
            { "EasyAntiCheat.exe", ("Easy Anti-Cheat (EAC)", RiskSeverity.Critical) },
            { "EasyAntiCheat_EOS.exe", ("Easy Anti-Cheat (EOS)", RiskSeverity.Critical) },
            { "EasyAntiCheat_EOS.sys", ("Easy Anti-Cheat Kernel Driver", RiskSeverity.Critical) },
            { "EasyAntiCheat_Setup.exe", ("Easy Anti-Cheat (EAC)", RiskSeverity.Critical) },
            { "eac_server.dll", ("Easy Anti-Cheat Server Module", RiskSeverity.Critical) },

            // BattlEye
            { "BEService.exe", ("BattlEye Anti-Cheat", RiskSeverity.Critical) },
            { "BEService_x64.exe", ("BattlEye Anti-Cheat", RiskSeverity.Critical) },
            { "BEDaisy.sys", ("BattlEye Kernel Driver", RiskSeverity.Critical) },
            { "BEClient_x64.dll", ("BattlEye Client Library", RiskSeverity.Critical) },

            // Riot Vanguard
            { "vgk.sys", ("Riot Vanguard Kernel System", RiskSeverity.Critical) },
            { "vgc.exe", ("Riot Vanguard Service", RiskSeverity.Critical) },

            // Call of Duty Ricochet
            { "randgrid.sys", ("Ricochet Anti-Cheat Kernel Driver", RiskSeverity.Critical) },

            // EA AntiCheat
            { "EAAntiCheat.GameService.exe", ("EA AntiCheat (EAAC)", RiskSeverity.Critical) },
            { "EAAntiCheat.sys", ("EA AntiCheat Kernel Driver", RiskSeverity.Critical) },
            { "EAAntiCheat.Installer.exe", ("EA AntiCheat (EAAC)", RiskSeverity.Critical) },

            // Valve Anti-Cheat / CS2
            { "vac.dll", ("Valve Anti-Cheat (VAC)", RiskSeverity.Critical) },

            // Tencent ACE / Anti-Cheat Expert
            { "AntiCheatExpert.sys", ("Tencent Anti-Cheat Expert (ACE)", RiskSeverity.Critical) },
            { "ACE-BASE.sys", ("Tencent Anti-Cheat Expert (ACE)", RiskSeverity.Critical) },

            // nProtect GameGuard & Xigncode
            { "Gamemon.des", ("nProtect GameGuard", RiskSeverity.Critical) },
            { "xigncode.sys", ("XIGNCODE3 Anti-Cheat", RiskSeverity.Critical) },
            { "x3.xem", ("XIGNCODE3 Anti-Cheat", RiskSeverity.Critical) },

            // Denuvo Anti-Cheat
            { "denuvo-anti-cheat.sys", ("Denuvo Anti-Cheat", RiskSeverity.Critical) }
        };

        private static readonly Dictionary<string, (string GameTitle, string SystemName, RiskSeverity Level)> CompetitiveGames = new(StringComparer.OrdinalIgnoreCase)
        {
            { "VALORANT-Win64-Shipping.exe", ("VALORANT", "Riot Vanguard Kernel Anti-Cheat", RiskSeverity.Critical) },
            { "VALORANT.exe", ("VALORANT", "Riot Vanguard Kernel Anti-Cheat", RiskSeverity.Critical) },
            { "FortniteClient-Win64-Shipping.exe", ("Fortnite", "Easy Anti-Cheat / BattlEye", RiskSeverity.Critical) },
            { "FortniteLauncher.exe", ("Fortnite", "Easy Anti-Cheat / BattlEye", RiskSeverity.Critical) },
            { "r5apex.exe", ("Apex Legends", "Easy Anti-Cheat (EAC)", RiskSeverity.Critical) },
            { "ApexLegends.exe", ("Apex Legends", "Easy Anti-Cheat (EAC)", RiskSeverity.Critical) },
            { "RainbowSix.exe", ("Rainbow Six Siege", "BattlEye Anti-Cheat", RiskSeverity.Critical) },
            { "RainbowSix_Vulkan.exe", ("Rainbow Six Siege", "BattlEye Anti-Cheat", RiskSeverity.Critical) },
            { "cs2.exe", ("Counter-Strike 2", "Valve Anti-Cheat / VACnet", RiskSeverity.Critical) },
            { "dota2.exe", ("Dota 2", "Valve Anti-Cheat (VAC)", RiskSeverity.Moderate) },
            { "Overwatch.exe", ("Overwatch 2", "Blizzard Defense Matrix", RiskSeverity.Critical) },
            { "TheFinals.exe", ("THE FINALS", "Easy Anti-Cheat (EAC)", RiskSeverity.Critical) },
            { "Discovery.exe", ("THE FINALS", "Easy Anti-Cheat (EAC)", RiskSeverity.Critical) },
            { "Warzone.exe", ("Call of Duty: Warzone", "Ricochet Kernel Anti-Cheat", RiskSeverity.Critical) },
            { "cod.exe", ("Call of Duty: Modern Warfare / Warzone", "Ricochet Kernel Anti-Cheat", RiskSeverity.Critical) },
            { "PUBG.exe", ("PUBG: BATTLEGROUNDS", "BattlEye & Zakynthos Anti-Cheat", RiskSeverity.Critical) },
            { "TslGame.exe", ("PUBG: BATTLEGROUNDS", "BattlEye & Zakynthos Anti-Cheat", RiskSeverity.Critical) },
            { "Destiny2.exe", ("Destiny 2", "BattlEye Anti-Cheat", RiskSeverity.Critical) },
            { "LeagueClient.exe", ("League of Legends", "Riot Vanguard Anti-Cheat", RiskSeverity.Critical) },
            { "GenshinImpact.exe", ("Genshin Impact", "HoYoverse Kernel Anti-Cheat (mhyprot)", RiskSeverity.Critical) },
            { "StarRail.exe", ("Honkai: Star Rail", "HoYoverse Kernel Anti-Cheat", RiskSeverity.Critical) },
            { "ZenlessZoneZero.exe", ("Zenless Zone Zero", "HoYoverse Kernel Anti-Cheat", RiskSeverity.Critical) },
            { "Helldivers2.exe", ("HELLDIVERS 2", "nProtect GameGuard", RiskSeverity.Critical) },
            { "Deadlock.exe", ("Deadlock", "Valve Anti-Cheat (VAC)", RiskSeverity.Critical) }
        };

        public static AntiCheatCheckResult ScanGame(string executablePath, string? installDirectory = null)
        {
            var result = new AntiCheatCheckResult();

            try
            {
                string exeName = Path.GetFileName(executablePath);
                string exeDir = installDirectory ?? Path.GetDirectoryName(executablePath) ?? string.Empty;

                // 1. Match executable against known competitive/multiplayer registry
                if (CompetitiveGames.TryGetValue(exeName, out var match))
                {
                    result.IsOnlineOrProtectedGame = true;
                    result.DetectedSystem = match.SystemName;
                    result.Level = match.Level;
                    result.Details = $"Known competitive multiplayer title '{match.GameTitle}' protected by {match.SystemName}.";
                    return result;
                }

                if (string.IsNullOrEmpty(exeDir) || !Directory.Exists(exeDir))
                {
                    return result;
                }

                // 2. Safe scan files in directory and immediate subdirectories (up to depth 2)
                var scannedFiles = Directory.EnumerateFiles(exeDir, "*.*", SafeEnumOptions);
                foreach (var file in scannedFiles)
                {
                    string fName = Path.GetFileName(file);
                    if (AntiCheatFiles.TryGetValue(fName, out var ac))
                    {
                        result.IsOnlineOrProtectedGame = true;
                        result.DetectedSystem = ac.SystemName;
                        result.Level = ac.Level;
                        result.Details = $"Anti-cheat binary '{fName}' detected in game directory.";
                        return result;
                    }
                }
            }
            catch
            {
            }

            return result;
        }
    }
}
