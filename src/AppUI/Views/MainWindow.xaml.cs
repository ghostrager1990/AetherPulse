using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using AppUI.ViewModels;

namespace AppUI.Views
{
    public partial class MainWindow : Window
    {
        private const int WM_GETMINMAXINFO = 0x0024;
        private const int MONITOR_DEFAULTTONEAREST = 0x00000002;
        private const int WM_USER = 0x0400;
        private const int WM_TRAYICON = WM_USER + 100;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_RBUTTONUP = 0x0205;

        private const uint NIM_ADD = 0x00000000;
        private const uint NIM_MODIFY = 0x00000001;
        private const uint NIM_DELETE = 0x00000002;

        private const uint NIF_MESSAGE = 0x00000001;
        private const uint NIF_ICON = 0x00000002;
        private const uint NIF_TIP = 0x00000004;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public int dwState;
            public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public int uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public int dwInfoFlags;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpdata);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        private NOTIFYICONDATA _nid;
        private bool _isTrayActive;
        private bool _isExplicitExit;
        private readonly MainViewModel _viewModel;
        private ContextMenu? _trayContextMenu;

        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            var handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(WindowProc);

            InitializeSystemTray(handle);
        }

        private void InitializeSystemTray(IntPtr hwnd)
        {
            IntPtr hIcon = IntPtr.Zero;
            string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app_icon.ico");
            if (System.IO.File.Exists(iconPath))
            {
                hIcon = ExtractIcon(IntPtr.Zero, iconPath, 0);
            }
            if (hIcon == IntPtr.Zero)
            {
                hIcon = LoadIcon(IntPtr.Zero, (IntPtr)32512); // IDI_APPLICATION fallback
            }

            _nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = hwnd,
                uID = 1001,
                uFlags = (int)(NIF_MESSAGE | NIF_ICON | NIF_TIP),
                uCallbackMessage = WM_TRAYICON,
                hIcon = hIcon,
                szTip = "AetherPulse — Frame Pacing & FidelityFX Bridge"
            };

            _isTrayActive = Shell_NotifyIcon(NIM_ADD, ref _nid);

            // Create WPF Context Menu for Tray
            _trayContextMenu = new ContextMenu();
            var openItem = new MenuItem { Header = "⚡ Open AetherPulse Dashboard", FontWeight = FontWeights.Bold };
            openItem.Click += (s, e) => RestoreFromTray();
            _trayContextMenu.Items.Add(openItem);

            var pacingItem = new MenuItem { Header = "⏱️ Active Frame Pacing: Enabled", IsEnabled = false };
            _trayContextMenu.Items.Add(pacingItem);

            _trayContextMenu.Items.Add(new Separator());

            var exitItem = new MenuItem { Header = "✕ Exit AetherPulse" };
            exitItem.Click += (s, e) => CompleteExit();
            _trayContextMenu.Items.Add(exitItem);
        }

        public void RestoreFromTray()
        {
            Show();
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Activate();
            Focus();
        }

        public void CompleteExit()
        {
            _isExplicitExit = true;
            if (_isTrayActive)
            {
                Shell_NotifyIcon(NIM_DELETE, ref _nid);
                _isTrayActive = false;
            }
            Application.Current.Shutdown();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_isExplicitExit && _viewModel.SettingsVM.CloseToTray)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            if (_isTrayActive)
            {
                Shell_NotifyIcon(NIM_DELETE, ref _nid);
                _isTrayActive = false;
            }

            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_isTrayActive)
            {
                Shell_NotifyIcon(NIM_DELETE, ref _nid);
                _isTrayActive = false;
            }

            base.OnClosed(e);
            Application.Current?.Shutdown();
        }

        private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_GETMINMAXINFO)
            {
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
            }
            else if (msg == WM_TRAYICON)
            {
                int mouseEvent = (int)lParam;
                if (mouseEvent == WM_LBUTTONUP || mouseEvent == WM_LBUTTONDBLCLK)
                {
                    RestoreFromTray();
                    handled = true;
                }
                else if (mouseEvent == WM_RBUTTONUP && _trayContextMenu != null)
                {
                    _trayContextMenu.IsOpen = true;
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        private static void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

            if (monitor != IntPtr.Zero)
            {
                var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(monitor, ref monitorInfo))
                {
                    var rcWorkArea = monitorInfo.rcWork;
                    var rcMonitorArea = monitorInfo.rcMonitor;

                    mmi.ptMaxPosition.x = Math.Abs(rcWorkArea.left - rcMonitorArea.left);
                    mmi.ptMaxPosition.y = Math.Abs(rcWorkArea.top - rcMonitorArea.top);
                    mmi.ptMaxSize.x = Math.Abs(rcWorkArea.right - rcWorkArea.left);
                    mmi.ptMaxSize.y = Math.Abs(rcWorkArea.bottom - rcWorkArea.top);
                }
            }

            Marshal.StructureToPtr(mmi, lParam, true);
        }

        private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (e.ClickCount == 2)
                {
                    ToggleMaximize();
                }
                else
                {
                    DragMove();
                }
            }
        }

        private void OnMinimizeClick(object sender, RoutedEventArgs e)
        {
            if (_viewModel.SettingsVM.MinimizeToTray)
            {
                Hide();
            }
            else
            {
                WindowState = WindowState.Minimized;
            }
        }

        private void OnMaximizeClick(object sender, RoutedEventArgs e)
        {
            ToggleMaximize();
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
