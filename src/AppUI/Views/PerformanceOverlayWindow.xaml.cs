using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace AppUI.Views
{
    public partial class PerformanceOverlayWindow : Window
    {
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int GWL_EXSTYLE = -20;
        private const int HOTKEY_ID = 9000;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint VK_OEM_4 = 0xDB;
        private const int WM_HOTKEY = 0x0312;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private DispatcherTimer _pollTimer;
        private IntPtr _hwnd;

        public PerformanceOverlayWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Closed += OnClosed;

            _pollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _pollTimer.Tick += OnPollTick;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;

            int extendedStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
            SetWindowLong(_hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);

            HwndSource source = HwndSource.FromHwnd(_hwnd);
            source?.AddHook(HwndHook);
            RegisterHotKey(_hwnd, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, VK_OEM_4);

            _pollTimer.Start();
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            _pollTimer.Stop();
            UnregisterHotKey(_hwnd, HOTKEY_ID);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                Visibility = (Visibility == Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible;
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void ResetToZero()
        {
            TxtFps.Text = "0.0";
            TxtFrametime.Text = "0.00 ms";
            TxtLowFps.Text = "0.0";
            TxtJitter.Text = "0.0%";
            TxtStatus.Text = "STANDBY";
        }

        private void OnPollTick(object? sender, EventArgs e)
        {
            try
            {
                string statusPath = @"C:\Users\Public\aetherpulse_status.json";
                if (!File.Exists(statusPath))
                {
                    ResetToZero();
                    return;
                }

                // Heartbeat check: If status file hasn't been touched in over 1 second, the game is closed or paused
                var fileInfo = new FileInfo(statusPath);
                if (DateTime.UtcNow - fileInfo.LastWriteTimeUtc > TimeSpan.FromSeconds(1.0))
                {
                    ResetToZero();
                    return;
                }

                using var stream = new FileStream(statusPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var doc = JsonDocument.Parse(stream);
                var root = doc.RootElement;

                int pid = root.GetProperty("pid").GetInt32();
                
                // Process check: Verify the hooked game process is actively alive
                bool processAlive = false;
                try
                {
                    var proc = Process.GetProcessById(pid);
                    processAlive = !proc.HasExited;
                }
                catch
                {
                    processAlive = false;
                }

                if (!processAlive)
                {
                    ResetToZero();
                    return;
                }

                double ft = root.GetProperty("frametimeMs").GetDouble();
                double low = root.GetProperty("onePercentLowFps").GetDouble();
                double jitter = root.GetProperty("stutterPercent").GetDouble();
                int target = root.GetProperty("targetFps").GetInt32();
                bool pacing = root.GetProperty("pacing").GetBoolean();

                double fps = ft > 0.001 ? (1000.0 / ft) : 0.0;

                TxtFps.Text = fps.ToString("F1");
                TxtFrametime.Text = $"{ft:F2} ms";
                TxtLowFps.Text = low.ToString("F1");
                TxtJitter.Text = $"{jitter:F1}%";
                TxtTarget.Text = target > 0 ? $"{target} FPS" : "Auto";
                TxtStatus.Text = pacing ? "PACING LOCKED" : "UNLOCKED";
            }
            catch
            {
                // File lock race; avoid crashing poll tick
            }
        }
    }
}
