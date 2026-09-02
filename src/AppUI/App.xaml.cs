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
            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                AppUI.Services.Telemetry.PresentMonCaptureService.Shutdown();
            };

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                MessageBox.Show($"Unhandled Domain Exception: {e.ExceptionObject}", "Fatal Fault", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            DispatcherUnhandledException += (s, e) =>
            {
                MessageBox.Show($"Dispatcher Exception: {e.Exception.Message}\n\n{e.Exception.StackTrace}", "Dispatcher Fault", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
                Shutdown();
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
                AppUI.Services.Pacing.PacingIpcService.Instance.Initialize();
                var telemetryService = new TelemetryService();
                var deploymentService = new DeploymentService();
                var profileStorage = new ProfileStorageService();
                var appSettings = new AppSettingsService();
                var sessionManager = new GameSessionManager(profileStorage, telemetryService);
                var hardwareService = new HardwareDetectionService();

                await hardwareService.DetectHardwareAsync();
                _mainViewModel = new MainViewModel(telemetryService, deploymentService, profileStorage, sessionManager, appSettings);

                await System.Threading.Tasks.Task.Delay(800);

                var mainWindow = new Views.MainWindow(_mainViewModel);
                Current.MainWindow = mainWindow;
                ShutdownMode = ShutdownMode.OnMainWindowClose;

                mainWindow.Show();
                var overlay = new AppUI.Views.PerformanceOverlayWindow(telemetryService);
                mainWindow.SetOverlayWindow(overlay);
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
                AppUI.Services.Telemetry.PresentMonCaptureService.Shutdown();
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

