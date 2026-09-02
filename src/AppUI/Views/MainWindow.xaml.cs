using System;
using System.Windows;
using System.Windows.Media.Imaging;

namespace AppUI.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow(ViewModels.MainViewModel viewModel)
        {
            InitializeComponent();
            LoadWindowIconSafe();
            DataContext = viewModel;
        }

        private void LoadWindowIconSafe()
        {
            try
            {
                var iconUri = new Uri("pack://application:,,,/AetherPulse;component/Assets/AppIcon.ico", UriKind.Absolute);
                this.Icon = BitmapFrame.Create(iconUri);
            }
            catch
            {
                // Graceful fallback: prevent startup crash if pack URI resolution fails
            }
        }

        private void OnTitleBarMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                DragMove();
            }
        }

        private void OnMinimizeClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void OnMaximizeClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized 
                ? WindowState.Normal 
                : WindowState.Maximized;
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        public PerformanceOverlayWindow? OverlayWindow { get; private set; }
        private System.Windows.Interop.HwndSource? _hwndSource;
        private const int HUD_HOTKEY_ID = 9001;
        private const int VK_OEM_4 = 0xDB; // '[' key for Ctrl+Shift+[

        public void SetOverlayWindow(PerformanceOverlayWindow overlay)
        {
            OverlayWindow = overlay;
            if (DataContext is ViewModels.MainViewModel vm)
            {
                overlay.CurrentPosition = vm.SettingsVM.HudPosition;
                overlay.SnapToCorner(vm.SettingsVM.HudPosition);
                overlay.SetHudVisibility(vm.SettingsVM.ShowFloatingHud);
            }
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            _hwndSource = System.Windows.Interop.HwndSource.FromHwnd(helper.Handle);
            _hwndSource?.AddHook(HwndHook);
            UnsafeNativeMethods.RegisterHotKey(helper.Handle, HUD_HOTKEY_ID, UnsafeNativeMethods.MOD_CONTROL | UnsafeNativeMethods.MOD_SHIFT, VK_OEM_4);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY && wParam.ToInt32() == HUD_HOTKEY_ID)
            {
                TogglePerformanceOverlay();
                handled = true;
            }
            return IntPtr.Zero;
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_hwndSource != null)
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                UnsafeNativeMethods.UnregisterHotKey(helper.Handle, HUD_HOTKEY_ID);
                _hwndSource.RemoveHook(HwndHook);
                _hwndSource = null;
            }
            OverlayWindow?.Close();
            base.OnClosed(e);
        }

        public void TogglePerformanceOverlay(object? sender = null)
        {
            if (OverlayWindow == null) return;

            bool targetState = sender is bool b 
                ? b 
                : OverlayWindow.Visibility != Visibility.Visible;

            OverlayWindow.SetHudVisibility(targetState);

            if (DataContext is ViewModels.MainViewModel vm && vm.SettingsVM.ShowFloatingHud != targetState)
            {
                vm.SettingsVM.ShowFloatingHud = targetState;
            }
        }

        public void SnapHudPosition(string position)
        {
            OverlayWindow?.SnapToCorner(position);
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