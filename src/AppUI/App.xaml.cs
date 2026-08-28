using System.IO;
using System;
using System.Windows;
using AppUI.Services;
using AppUI.ViewModels;
using AppUI.Views;

namespace AppUI
{
    public partial class App : Application
    {
        private MainViewModel? _mainViewModel;

        public App()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[FATAL CRASH] {e.ExceptionObject}");
                Console.ResetColor();
            };

            DispatcherUnhandledException += (s, e) =>
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[DISPATCHER CRASH] {e.Exception}");
                Console.ResetColor();
                e.Handled = false;
            };
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var splashWindow = new SplashWindow();
            splashWindow.Show();

            string statusFile = @"C:\Users\Public\aetherpulse_status.json";
if (File.Exists(statusFile))
{
    try { File.Delete(statusFile); } catch { }
}

            try
            {
                var telemetryService = new TelemetryService();
                var deploymentService = new DeploymentService();
                var profileStorage = new ProfileStorageService();
                var appSettings = new AppSettingsService();
                var hardwareService = new HardwareDetectionService();

                await hardwareService.DetectHardwareAsync();
                _mainViewModel = new MainViewModel(telemetryService, deploymentService, profileStorage, appSettings);

                // Brief hold for visual smoothness
                await System.Threading.Tasks.Task.Delay(800);

                var mainWindow = new MainWindow(_mainViewModel);
                Current.MainWindow = mainWindow;
                ShutdownMode = ShutdownMode.OnMainWindowClose;

                MainWindow.Show();
            var overlay = new AppUI.Views.PerformanceOverlayWindow();
            overlay.Show();
                splashWindow.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Startup Error: {ex.Message}\n\nStack: {ex.StackTrace}", "AetherPulse Startup Fault", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _mainViewModel?.Dispose();
            }
            catch
            {
            }

            base.OnExit(e);
            Environment.Exit(0);
        }
    }
}


