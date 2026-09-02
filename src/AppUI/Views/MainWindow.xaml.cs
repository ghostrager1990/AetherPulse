using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using AppUI.ViewModels;

namespace AppUI.Views
{
    public partial class MainWindow : Window
    {
        private const int HotkeyId = 9000;
        private readonly MainViewModel _viewModel;

        public MainWindow(MainViewModel viewModel)
        {
            _viewModel = viewModel;
            DataContext = _viewModel;
            InitializeComponent();
            Loaded += OnMainWindowLoaded;
            Closing += OnMainWindowClosing;
        }

        private void OnMainWindowLoaded(object sender, RoutedEventArgs e)
        {
            var helper = new WindowInteropHelper(this);
            IntPtr hwnd = helper.Handle;
            UnsafeNativeMethods.RegisterHotKey(hwnd, HotkeyId, UnsafeNativeMethods.MOD_CONTROL | UnsafeNativeMethods.MOD_SHIFT, 0xDB);
            HwndSource.FromHwnd(hwnd)?.AddHook(HwndHook);
        }

        private void OnMainWindowClosing(object? sender, CancelEventArgs e)
        {
            var helper = new WindowInteropHelper(this);
            UnsafeNativeMethods.UnregisterHotKey(helper.Handle, HotkeyId);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x312;
            if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            {
                TogglePerformanceOverlayShortcut();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void OnSourceInitialized(object? sender, EventArgs e) { }

        private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void OnMaximizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

        public void TogglePerformanceOverlay(bool show)
        {
            foreach (Window win in Application.Current.Windows)
            {
                if (win is PerformanceOverlayWindow overlay)
                {
                    if (show) overlay.Show();
                    else overlay.Hide();
                    break;
                }
            }
        }

        private void TogglePerformanceOverlayShortcut()
        {
            foreach (Window win in Application.Current.Windows)
            {
                if (win is PerformanceOverlayWindow overlay)
                {
                    if (overlay.IsVisible)
                        overlay.Hide();
                    else
                        overlay.Show();
                    break;
                }
            }
        }

        public void SnapHudPosition(string position)
        {
            Window? targetOverlay = null;
            foreach (Window win in Application.Current.Windows)
            {
                if (win is PerformanceOverlayWindow)
                {
                    targetOverlay = win;
                    break;
                }
            }

            if (targetOverlay == null) return;

            double screenWidth = SystemParameters.WorkArea.Width;
            double screenHeight = SystemParameters.WorkArea.Height;

            switch (position)
            {
                case "Top Left":
                    targetOverlay.Left = 20;
                    targetOverlay.Top = 20;
                    break;
                case "Top Right":
                    targetOverlay.Left = screenWidth - targetOverlay.ActualWidth - 20;
                    targetOverlay.Top = 20;
                    break;
                case "Bottom Left":
                    targetOverlay.Left = 20;
                    targetOverlay.Top = screenHeight - targetOverlay.ActualHeight - 20;
                    break;
                case "Bottom Right":
                default:
                    targetOverlay.Left = screenWidth - targetOverlay.ActualWidth - 20;
                    targetOverlay.Top = screenHeight - targetOverlay.ActualHeight - 20;
                    break;
            }
        }
    }

    internal static class UnsafeNativeMethods
    {
        public const int MOD_CONTROL = 0x0002;
        public const int MOD_SHIFT = 0x0004;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
