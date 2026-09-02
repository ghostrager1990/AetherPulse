using System;
using System.IO;
using System.Security.Principal;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace AppUI.Services
{
    public class AppSettings
    {
        public bool StartWithWindows { get; set; } = false;
        public bool MinimizeToTray { get; set; } = true;
        public bool CloseToTray { get; set; } = true;
        public bool StartMinimized { get; set; } = false;
        public bool EnableProxyChaining { get; set; } = true;
        public bool EnableHardwareSensorPolling { get; set; } = true;
        public bool ShowFloatingHud { get; set; } = true;
        public string HudPosition { get; set; } = "Top Right";
    }

    public interface IAppSettingsService
    {
        AppSettings CurrentSettings { get; }
        bool IsRunningAsAdmin { get; }
        void Load();
        void Save();
        Task LoadSettingsAsync();
        Task SaveSettingsAsync();
        void ApplyStartupRegistry(bool enable);
    }

    public class AppSettingsService : IAppSettingsService
    {
        private const string RunRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "AetherPulse";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public string SettingsFilePath { get; }
        public AppSettings CurrentSettings { get; private set; } = new();

        public bool IsRunningAsAdmin
        {
            get
            {
                try
                {
                    using var identity = WindowsIdentity.GetCurrent();
                    var principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
                catch
                {
                    return false;
                }
            }
        }

        public AppSettingsService(string? customPath = null)
        {
            if (!string.IsNullOrEmpty(customPath))
            {
                SettingsFilePath = customPath;
            }
            else
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string dir = Path.Combine(localAppData, "AetherPulse");
                Directory.CreateDirectory(dir);
                SettingsFilePath = Path.Combine(dir, "settings.json");
            }

            Load();
        }

        public void Load()
        {
            if (!File.Exists(SettingsFilePath))
            {
                CurrentSettings = new AppSettings();
                return;
            }

            try
            {
                string json = File.ReadAllText(SettingsFilePath);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    CurrentSettings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                }
            }
            catch
            {
                CurrentSettings = new AppSettings();
            }
        }

        public void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(SettingsFilePath)!;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonSerializer.Serialize(CurrentSettings, JsonOptions);
                File.WriteAllText(SettingsFilePath, json);
                ApplyStartupRegistry(CurrentSettings.StartWithWindows);
            }
            catch
            {
            }
        }

        public async Task LoadSettingsAsync()
        {
            if (!File.Exists(SettingsFilePath))
            {
                CurrentSettings = new AppSettings();
                return;
            }

            try
            {
                string json = await File.ReadAllTextAsync(SettingsFilePath).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    CurrentSettings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                }
            }
            catch
            {
                CurrentSettings = new AppSettings();
            }
        }

        public async Task SaveSettingsAsync()
        {
            try
            {
                string dir = Path.GetDirectoryName(SettingsFilePath)!;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonSerializer.Serialize(CurrentSettings, JsonOptions);
                await File.WriteAllTextAsync(SettingsFilePath, json).ConfigureAwait(false);
                ApplyStartupRegistry(CurrentSettings.StartWithWindows);
            }
            catch
            {
            }
        }

        public void ApplyStartupRegistry(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
                if (key == null) return;

                if (enable)
                {
                    string exePath = Environment.ProcessPath ?? "";
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        key.SetValue(AppName, $"\"{exePath}\" --minimized");
                    }
                }
                else
                {
                    if (key.GetValue(AppName) != null)
                    {
                        key.DeleteValue(AppName, false);
                    }
                }
            }
            catch
            {
            }
        }
    }
}
