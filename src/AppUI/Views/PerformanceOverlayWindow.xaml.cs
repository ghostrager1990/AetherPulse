using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using AppUI.Models;
using AppUI.Services;

namespace AppUI.Views
{
    public partial class PerformanceOverlayWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private static readonly SolidColorBrush ActiveGreen = new(Color.FromRgb(0x3F, 0xB9, 0x50));
        private static readonly SolidColorBrush ActiveGreenBg = new(Color.FromRgb(0x1C, 0x2F, 0x24));
        private static readonly SolidColorBrush ActiveGreenBorder = new(Color.FromRgb(0x23, 0x86, 0x36));

        private static readonly SolidColorBrush StandbyAmber = new(Color.FromRgb(0xE3, 0xB3, 0x41));
        private static readonly SolidColorBrush StandbyAmberBg = new(Color.FromRgb(0x2A, 0x18, 0x00));
        private static readonly SolidColorBrush StandbyAmberBorder = new(Color.FromRgb(0x9E, 0x6A, 0x03));

        public string CurrentPosition { get; set; } = "Top Right";

        public PerformanceOverlayWindow(ITelemetryService? telemetryService = null)
        {
            InitializeComponent();
            Loaded += OnWindowLoaded;
            MouseLeftButtonDown += (s, e) =>
            {
                if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
                {
                    DragMove();
                }
            };

            if (telemetryService != null)
            {
                telemetryService.TelemetryUpdated += (data) =>
                {
                    float activeFps = data.CurrentFps > 0 ? data.CurrentFps : data.AverageFps;
                    UpdateMetrics(activeFps, data.FrameTimeMs, data.AverageFps, data.PacingJitterMs, data.PacerActive || data.FrameIndex > 0, data.CadenceRatio, data.SubFrameVarianceUs, data.RealTimeDeltaMs);
                    bool isLive = activeFps > 0.1f && (data.PacerActive || data.FrameIndex > 0);
                    SetPacingState(isLive);
                };
                telemetryService.ConnectionStatusChanged += (isConnected) =>
                {
                    if (!isConnected)
                    {
                        UpdateMetrics(0, 0, 0, 0, false, 0.50, 0.0, 0.0);
                    }
                };
            }
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            var helper = new WindowInteropHelper(this);
            int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
            SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
            SnapToCorner(CurrentPosition);
        }

        public void SetHudVisibility(bool isVisible)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (isVisible)
                {
                    Show();
                    Visibility = Visibility.Visible;
                }
                else
                {
                    Visibility = Visibility.Collapsed;
                }
            });
        }

        public void SnapToCorner(string position)
        {
            if (string.IsNullOrWhiteSpace(position)) return;
            CurrentPosition = position;

            Dispatcher.BeginInvoke(() =>
            {
                var workArea = SystemParameters.WorkArea;
                const double margin = 24.0;
                double width = ActualWidth > 0 ? ActualWidth : (Width > 0 ? Width : 340);
                double height = ActualHeight > 0 ? ActualHeight : (Height > 0 ? Height : 115);

                switch (position.Trim().ToLowerInvariant())
                {
                    case "top left":
                        Left = workArea.Left + margin;
                        Top = workArea.Top + margin;
                        break;
                    case "bottom left":
                        Left = workArea.Left + margin;
                        Top = workArea.Top + workArea.Height - height - margin;
                        break;
                    case "bottom right":
                        Left = workArea.Left + workArea.Width - width - margin;
                        Top = workArea.Top + workArea.Height - height - margin;
                        break;
                    case "top right":
                    default:
                        Left = workArea.Left + workArea.Width - width - margin;
                        Top = workArea.Top + margin;
                        break;
                }
            });
        }

        public void UpdateMetrics(double fps, double frameTimeMs, double lowFps, double jitterPercent, bool isHooked, double cadenceRatio = 0.50, double subFrameVarianceUs = 0.0, double realTimeDeltaMs = 0.0)
        {
            Dispatcher.BeginInvoke(() =>
            {
                TxtFps.Text = fps.ToString("F1");
                TxtFrametime.Text = $"{frameTimeMs:F2} ms";
                TxtLowFps.Text = lowFps.ToString("F1");

                if (subFrameVarianceUs > 0)
                {
                    TxtJitter.Text = $"{subFrameVarianceUs:F0} -s";
                }
                else
                {
                    TxtJitter.Text = $"{jitterPercent:F1}%";
                }

                if (realTimeDeltaMs > 0.001)
                {
                    TxtDelta.Text = $"{realTimeDeltaMs:F2}ms";
                }
                else
                {
                    TxtDelta.Text = $"{frameTimeMs:F2}ms";
                }

                // Format Cadence Ratio (e.g. 50:50, 52:48, 1.00x)
                if (cadenceRatio > 0.05 && cadenceRatio < 0.95)
                {
                    int pct1 = (int)Math.Round(cadenceRatio * 100.0);
                    int pct2 = 100 - pct1;
                    TxtCadence.Text = $"{pct1}:{pct2}";
                }
                else
                {
                    TxtCadence.Text = "50:50";
                }

                var ipc = AppUI.Services.Pacing.PacingIpcService.Instance.ReadCurrentIPC();
                bool effectiveHooked = isHooked || ipc.IsHookActive == 1;

                if (effectiveHooked)
                {
                    TxtStatus.Text = "ACTIVE (HOOKED)";
                    TxtStatus.Foreground = ActiveGreen;
                    BadgeStatus.Background = ActiveGreenBg;
                    BadgeStatus.BorderBrush = ActiveGreenBorder;
                }
                else
                {
                    TxtStatus.Text = "STANDBY";
                    TxtStatus.Foreground = StandbyAmber;
                    BadgeStatus.Background = StandbyAmberBg;
                    BadgeStatus.BorderBrush = StandbyAmberBorder;
                }
            });
        }

        public void SetPacingState(bool isActive)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (isActive)
                {
                    TxtPacingMode.Text = "CADENCE PACING: ACTIVE";
                    TxtPacingMode.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#58A6FF"));
                    ChipPacing.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#162235"));
                    ChipPacing.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1F6FEB"));
                    ChipPacing.Opacity = 1.0;
                }
                else
                {
                    TxtPacingMode.Text = "CADENCE PACING: INACTIVE";
                    TxtPacingMode.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8B949E"));
                    ChipPacing.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#161B22"));
                    ChipPacing.BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#30363D"));
                    ChipPacing.Opacity = 0.5;
                }
            });
        }
        public void UpdatePipelineStatus(bool isPacingEnabled, bool isRcasEnabled = false, bool isRrEnabled = false)
        {
            SetPacingState(isPacingEnabled);
        }

        public void UpdatePipelineStatus(bool isPacingEnabled, bool isRcasEnabled, float rcasSharpness, bool autoLod, float lodBias, bool isRrEnabled)
        {
            UpdatePipelineStatus(isPacingEnabled);
        }
    }
}


