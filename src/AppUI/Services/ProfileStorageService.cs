using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AppUI.Models;

namespace AppUI.Services
{
    public interface IProfileStorageService
    {
        string ProfilesFilePath { get; }
        string IgnoredGamesFilePath { get; }
        Task<List<GameProfile>> LoadProfilesAsync();
        Task SaveProfilesAsync(IEnumerable<GameProfile> profiles);
        Task<HashSet<string>> LoadIgnoredGamesAsync();
        Task SaveIgnoredGamesAsync(IEnumerable<string> paths);
    }

    public class ProfileStorageService : IProfileStorageService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public string ProfilesFilePath { get; }
        public string IgnoredGamesFilePath { get; }

        public ProfileStorageService(string? customPath = null)
        {
            if (!string.IsNullOrEmpty(customPath))
            {
                ProfilesFilePath = customPath;
                string dir = Path.GetDirectoryName(customPath) ?? AppDomain.CurrentDomain.BaseDirectory;
                IgnoredGamesFilePath = Path.Combine(dir, "ignored_games.json");
            }
            else
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string aetherPulseDir = Path.Combine(localAppData, "AetherPulse");
                Directory.CreateDirectory(aetherPulseDir);
                ProfilesFilePath = Path.Combine(aetherPulseDir, "profiles.json");
                IgnoredGamesFilePath = Path.Combine(aetherPulseDir, "ignored_games.json");
            }
        }

        public async Task<List<GameProfile>> LoadProfilesAsync()
        {
            if (!File.Exists(ProfilesFilePath))
            {
                return new List<GameProfile>();
            }

            try
            {
                string json = await File.ReadAllTextAsync(ProfilesFilePath).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return new List<GameProfile>();
                }

                var profiles = JsonSerializer.Deserialize<List<GameProfile>>(json, JsonOptions);
                if (profiles != null)
                {
                    foreach (var p in profiles)
                    {
                        if (p.Mode == DeploymentMode.VersionProxy || p.Mode == DeploymentMode.Both)
                        {
                            p.Mode = DeploymentMode.DxcoreProxy;
                        }
                    }
                    return profiles;
                }
                return new List<GameProfile>();
            }
            catch
            {
                return new List<GameProfile>();
            }
        }

        public async Task SaveProfilesAsync(IEnumerable<GameProfile> profiles)
        {
            try
            {
                string dir = Path.GetDirectoryName(ProfilesFilePath)!;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonSerializer.Serialize(profiles, JsonOptions);
                await File.WriteAllTextAsync(ProfilesFilePath, json).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        public async Task<HashSet<string>> LoadIgnoredGamesAsync()
        {
            if (!File.Exists(IgnoredGamesFilePath))
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                string json = await File.ReadAllTextAsync(IgnoredGamesFilePath).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }

                var list = JsonSerializer.Deserialize<List<string>>(json, JsonOptions);
                return list != null ? new HashSet<string>(list, StringComparer.OrdinalIgnoreCase) : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public async Task SaveIgnoredGamesAsync(IEnumerable<string> paths)
        {
            try
            {
                string dir = Path.GetDirectoryName(IgnoredGamesFilePath)!;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonSerializer.Serialize(paths, JsonOptions);
                await File.WriteAllTextAsync(IgnoredGamesFilePath, json).ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }
}
