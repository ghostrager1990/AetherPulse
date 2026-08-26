using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;

namespace AppUI.Services
{
    public interface IElevationService
    {
        bool IsRunningAsAdministrator();
        bool RestartAsAdministrator();
    }

    public class ElevationService : IElevationService
    {
        public bool IsRunningAsAdministrator()
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

        public bool RestartAsAdministrator()
        {
            try
            {
                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath))
                {
                    return false;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true,
                    Verb = "runas"
                };

                Process.Start(startInfo);
                Application.Current?.Dispatcher?.Invoke(() => Application.Current.Shutdown());
                return true;
            }
            catch (Exception)
            {
                // User cancelled UAC prompt or access denied
                return false;
            }
        }
    }
}
