using BNKaraoke.DJ.Services;
using Microsoft.Win32;
using Serilog;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace BNKaraoke.DJ.Views
{
    public partial class BrandScreenWindow : Window
    {
        private readonly SettingsService _settingsService = SettingsService.Instance;
        private HwndSource? _windowSource;
        private IntPtr _windowHandle;
        private bool _noActivateApplied;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WM_MOUSEACTIVATE = 0x0021;
        private const int MA_NOACTIVATE = 3;
        private const uint MONITOR_DEFAULTTONEAREST = 2;

        [DllImport("User32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

        [DllImport("Shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, DpiType dpiType, out uint dpiX, out uint dpiY);

        private enum DpiType
        {
            Effective = 0,
            Angular = 1,
            Raw = 2,
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            return IntPtr.Size == 8
                ? GetWindowLongPtr64(hWnd, nIndex)
                : new IntPtr(GetWindowLong32(hWnd, nIndex));
        }

        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            return IntPtr.Size == 8
                ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
                : new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
        }

        public BrandScreenWindow()
        {
            InitializeComponent();
            Opacity = 0;

            SourceInitialized += BrandScreenWindow_SourceInitialized;
            Loaded += BrandScreenWindow_Loaded;
            _settingsService.VideoDeviceChanged += SettingsService_VideoDeviceChanged;
            SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
        }

        public void ActivateSurface()
        {
            void Apply()
            {
                if (!IsVisible)
                {
                    Show();
                    EnsureShownBeforeMaximize();
                    SetDisplayDevice();
                }

                if (Opacity != 1)
                {
                    Opacity = 1;
                }

                if (!IsVisible)
                {
                    Show();
                }

                BringToFront();
            }

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(Apply, DispatcherPriority.Render);
            }
            else
            {
                Apply();
            }
        }

        public void DeactivateSurface()
        {
            void Apply()
            {
                if (Opacity != 0)
                {
                    Opacity = 0;
                }

                if (IsVisible)
                {
                    Hide();
                }
            }

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(Apply, DispatcherPriority.Render);
            }
            else
            {
                Apply();
            }
        }

        public void ShowWindow()
        {
            Log.Information("[BRAND SCREEN] Preparing to show brand screen window");
            WindowStyle = WindowStyle.None;
            EnsureShownBeforeMaximize();
            SetDisplayDevice();
        }

        public void SafeClose()
        {
            void CloseWindow()
            {
                if (IsVisible)
                {
                    Close();
                }
                else
                {
                    // Close still needs to run to remove message hooks.
                    Close();
                }
            }

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(CloseWindow);
            }
            else
            {
                CloseWindow();
            }
        }

        internal void SetDisplayDevice()
        {
            try
            {
                var targetDevice = _settingsService.Settings.KaraokeVideoDevice;
                var hwnd = new WindowInteropHelper(this).Handle;
                var screens = System.Windows.Forms.Screen.AllScreens;
                if (screens == null || screens.Length == 0)
                {
                    Log.Warning("[BRAND SCREEN] No displays detected when attempting to position the brand window");
                    return;
                }

                var targetScreen = screens.FirstOrDefault(screen =>
                        !string.IsNullOrWhiteSpace(targetDevice) &&
                        screen.DeviceName.Equals(targetDevice, StringComparison.OrdinalIgnoreCase));
                if (targetScreen == null)
                {
                    Log.Warning("[BRAND SCREEN] Target display not found. Requested={Requested}, Available={Available}. Falling back to current/primary.",
                        targetDevice,
                        string.Join(", ", screens.Select(s => s.DeviceName)));
                    targetScreen = hwnd != IntPtr.Zero
                        ? System.Windows.Forms.Screen.FromHandle(hwnd)
                        : System.Windows.Forms.Screen.PrimaryScreen ?? screens.First();
                }

                ApplyScreenBounds(targetScreen);
            }
            catch (Exception ex)
            {
                Log.Error("[BRAND SCREEN] Failed to set display device: {Message}", ex.Message);
            }
        }

        private void ApplyScreenBounds(System.Windows.Forms.Screen targetScreen)
        {
            try
            {
                var bounds = targetScreen.Bounds;
                var source = PresentationSource.FromVisual(this);
                var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;

                var topLeft = transform.Transform(new Point(bounds.Left, bounds.Top));
                var bottomRight = transform.Transform(new Point(bounds.Right, bounds.Bottom));

                Left = topLeft.X;
                Top = topLeft.Y;
                Width = Math.Abs(bottomRight.X - topLeft.X);
                Height = Math.Abs(bottomRight.Y - topLeft.Y);

                var dpi = VisualTreeHelper.GetDpi(this);
                Log.Information("[BRAND SCREEN] Positioned to {Device} -> {Left}x{Top} {Width}x{Height} (DPI {DpiX}x{DpiY})",
                    targetScreen.DeviceName, Left, Top, Width, Height, dpi.PixelsPerInchX, dpi.PixelsPerInchY);
            }
            catch (Exception ex)
            {
                Log.Warning("[BRAND SCREEN] Failed to apply screen bounds: {Message}", ex.Message);
            }
        }

        private void BrandScreenWindow_SourceInitialized(object? sender, EventArgs e)
        {
            try
            {
                Log.Information("[BRAND SCREEN] Source initialized, capturing handles");
                _windowHandle = new WindowInteropHelper(this).Handle;
                InitializeDisplayOnlyWindow();
            }
            catch (Exception ex)
            {
                Log.Error("[BRAND SCREEN] Failed during source initialization: {Message}", ex.Message);
            }
        }

        private void BrandScreenWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Log.Information("[BRAND SCREEN] Window loaded");
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                EnsureShownBeforeMaximize();
                SetDisplayDevice();

                Visibility = Visibility.Visible;
                ShowActivated = false;
                ApplyNoActivateStyle();
            }
            catch (Exception ex)
            {
                Log.Error("[BRAND SCREEN] Failed during load: {Message}", ex.Message);
            }
        }

        private void SettingsService_VideoDeviceChanged(object? sender, string deviceId)
        {
            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    Log.Information("[BRAND SCREEN] Video device changed to {Device}. Repositioning window.", deviceId);
                    SetDisplayDevice();
                }
                catch (Exception ex)
                {
                    Log.Error("[BRAND SCREEN] Failed to apply video device change: {Message}", ex.Message);
                }
            });
        }

        private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (!IsLoaded)
                {
                    return;
                }

                try
                {
                    Log.Information("[BRAND SCREEN] Display configuration changed. Reapplying placement.");
                    SetDisplayDevice();
                }
                catch (Exception ex)
                {
                    Log.Error("[BRAND SCREEN] Failed to adjust after display change: {Message}", ex.Message);
                }
            });
        }

        private void InitializeDisplayOnlyWindow()
        {
            try
            {
                if (_windowSource != null)
                {
                    _windowSource.RemoveHook(WindowProc);
                }

                _windowSource = PresentationSource.FromVisual(this) as HwndSource;
                if (_windowSource != null)
                {
                    _windowSource.AddHook(WindowProc);
                    Log.Information("[BRAND SCREEN] Window message hook installed");
                }
            }
            catch (Exception ex)
            {
                Log.Error("[BRAND SCREEN] Failed to initialize display-only behaviors: {Message}", ex.Message);
            }
        }

        private void EnsureShownBeforeMaximize()
        {
            var originallyShowActivated = ShowActivated;
            var originalOpacity = Opacity;
            bool opacityAdjusted = false;

            bool requiresStateChange = !IsVisible || WindowState != WindowState.Normal;
            if (requiresStateChange)
            {
                Opacity = 0;
                opacityAdjusted = true;
            }

            ShowActivated = true;

            if (!IsVisible)
            {
                WindowState = WindowState.Normal;
                Show();
                Log.Information("[BRAND SCREEN] Shown Normal; deferring maximize");
            }
            else if (WindowState != WindowState.Normal)
            {
                WindowState = WindowState.Normal;
                Log.Information("[BRAND SCREEN] Window made normal before maximize; current state={State}", WindowState);
            }
            else
            {
                Log.Information("[BRAND SCREEN] Window already visible; deferring maximize");
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    WindowState = WindowState.Maximized;
                }
                finally
                {
                    if (opacityAdjusted)
                    {
                        Opacity = originalOpacity;
                    }

                    ShowActivated = originallyShowActivated;
                    Log.Information("[BRAND SCREEN] Maximized; final WindowState={WindowState}, ShowActivated={ShowActivated}",
                        WindowState, ShowActivated);
                }
            }), DispatcherPriority.Render);
        }

        private void ApplyNoActivateStyle()
        {
            try
            {
                if (_windowHandle == IntPtr.Zero || _noActivateApplied)
                {
                    return;
                }

                var exStyle = GetWindowLongPtr(_windowHandle, GWL_EXSTYLE);
                var newStyle = new IntPtr(exStyle.ToInt64() | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT);
                SetWindowLongPtr(_windowHandle, GWL_EXSTYLE, newStyle);
                _noActivateApplied = true;
                Log.Information("[BRAND SCREEN] Applied WS_EX_NOACTIVATE to prevent focus stealing");
            }
            catch (Exception ex)
            {
                Log.Error("[BRAND SCREEN] Failed to apply no-activate style: {Message}", ex.Message);
            }
        }

        private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_MOUSEACTIVATE)
            {
                handled = true;
                return new IntPtr(MA_NOACTIVATE);
            }

            return IntPtr.Zero;
        }

        private void BringToFront()
        {
            try
            {
                if (_windowHandle == IntPtr.Zero)
                {
                    _windowHandle = new WindowInteropHelper(this).Handle;
                }

                if (_windowHandle == IntPtr.Zero)
                {
                    return;
                }

                const uint SWP_NOSIZE = 0x0001;
                const uint SWP_NOMOVE = 0x0002;
                const uint SWP_NOACTIVATE = 0x0010;
                const uint SWP_SHOWWINDOW = 0x0040;

                SetWindowPos(_windowHandle, new IntPtr(-1), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
            catch (Exception ex)
            {
                Log.Debug("[BRAND SCREEN] Failed to bring window to front: {Message}", ex.Message);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                Log.Information("[BRAND SCREEN] Closing brand window");
                if (_windowSource != null)
                {
                    _windowSource.RemoveHook(WindowProc);
                    _windowSource = null;
                }

                _settingsService.VideoDeviceChanged -= SettingsService_VideoDeviceChanged;
                SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
            }
            catch (Exception ex)
            {
                Log.Error("[BRAND SCREEN] Error during cleanup: {Message}", ex.Message);
            }

            base.OnClosed(e);
        }
    }
}
