using BNKaraoke.DJ.Models;
using BNKaraoke.DJ.Services;
using BNKaraoke.DJ.ViewModels.Overlays;
using LibVLCSharp.Shared;
using Serilog;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace BNKaraoke.DJ.Views
{
    public partial class VideoPlayerWindow : Window
    {
        private readonly SettingsService _settingsService = SettingsService.Instance;
        private LibVLC? _libVLC;
        private readonly string? _libVlcPluginDirectory;
        public LibVLCSharp.Shared.MediaPlayer? MediaPlayer { get; private set; }
        private Media? _currentMedia;
        private string? _currentVideoPath;
        private bool _showBrandOnStopEvent = true;
        private long _currentPosition;
        private DispatcherTimer? _hideVideoViewTimer;
        private Equalizer? _equalizer;
        private readonly object _audioSelectionLock = new();
        private bool _suppressSongEnded;
        private bool _lastPlaybackUsedHardwareDecoding;
        private bool _hasTriedSoftwareFallbackForCurrentMedia;
        private HwndSource? _windowSource;
        private IntPtr _windowHandle;
        private IntPtr _controllerWindowHandle;
        private readonly OverlayViewModel _overlayVm = OverlayViewModel.Instance;

        public event EventHandler? SongEnded;
        public new event EventHandler? Closed;
        public event EventHandler<MediaPlayerPositionChangedEventArgs>? PositionChanged;
        public event EventHandler? MediaPlayerReinitialized;
        public event EventHandler<long>? MediaLengthChanged;

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

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hWnd);

        [Flags]
        private enum SetWindowPosFlags : uint
        {
            SWP_NOSIZE = 0x0001,
            SWP_NOMOVE = 0x0002,
            SWP_NOZORDER = 0x0004,
            SWP_NOREDRAW = 0x0008,
            SWP_NOACTIVATE = 0x0010,
            SWP_FRAMECHANGED = 0x0020,
            SWP_SHOWWINDOW = 0x0040,
            SWP_HIDEWINDOW = 0x0080,
            SWP_NOCOPYBITS = 0x0100,
            SWP_NOOWNERZORDER = 0x0200,
            SWP_NOSENDCHANGING = 0x0400
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WM_MOUSEACTIVATE = 0x0021;
        private const int WM_ACTIVATE = 0x0006;
        private const int WM_SETFOCUS = 0x0007;
        private const int WM_NCACTIVATE = 0x0086;
        private const int MA_NOACTIVATE = 3;
        private const int WA_INACTIVE = 0;

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

        private void CaptureControllerWindowHandle()
        {
            try
            {
                if (Application.Current?.MainWindow != null)
                {
                    var handle = new WindowInteropHelper(Application.Current.MainWindow).Handle;
                    if (handle != IntPtr.Zero)
                    {
                        _controllerWindowHandle = handle;
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("[VIDEO PLAYER] Unable to capture main window handle: {Message}", ex.Message);
            }

            try
            {
                var foreground = GetForegroundWindow();
                if (foreground != IntPtr.Zero)
                {
                    _controllerWindowHandle = foreground;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("[VIDEO PLAYER] Unable to capture foreground window handle: {Message}", ex.Message);
            }
        }

        private static string? ResolveLibVlcPluginDirectory()
        {
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var candidateRoots = new List<string>
            {
                Path.Combine(baseDirectory, "TOOLS"),
                Path.Combine(baseDirectory, "Tools"),
                Path.Combine(baseDirectory, "libvlc"),
                Path.Combine(baseDirectory, "libvlc", "win-x64"),
                Path.Combine(baseDirectory, "runtimes", "win-x64", "native"),
                baseDirectory
            };

            foreach (var candidate in candidateRoots)
            {
                var pluginDirectory = TryResolvePluginDirectory(candidate);
                if (!string.IsNullOrWhiteSpace(pluginDirectory))
                {
                    return pluginDirectory;
                }
            }

            try
            {
                var discovered = Directory.EnumerateDirectories(baseDirectory, "plugins", SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(discovered))
                {
                    return discovered;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("[VIDEO PLAYER] Failed to probe for LibVLC plugins directory: {Message}", ex.Message);
            }

            return null;
        }

        private static string? TryResolvePluginDirectory(string? root)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                return null;
            }

            var rootDirectoryName = Path.GetFileName(root!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrWhiteSpace(rootDirectoryName) &&
                rootDirectoryName.Equals("plugins", StringComparison.OrdinalIgnoreCase))
            {
                return root!;
            }

            var directPlugins = Path.Combine(root!, "plugins");
            if (Directory.Exists(directPlugins))
            {
                return directPlugins;
            }

            var vlcPlugins = Path.Combine(root!, "vlc", "plugins");
            if (Directory.Exists(vlcPlugins))
            {
                return vlcPlugins;
            }

            return null;
        }

        private static string? CreateLibVlcOption(string optionName, string? value)
        {
            if (string.IsNullOrWhiteSpace(optionName) || string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var escapedValue = value.Replace("\"", "\\\"");
            return $"{optionName}=\"{escapedValue}\"";
        }

        private static void AddOptionIfPresent(List<string> options, string? option)
        {
            if (!string.IsNullOrWhiteSpace(option))
            {
                options.Add(option);
            }
        }

        private void InitializeDisplayOnlyWindow()
        {
            if (_windowHandle == IntPtr.Zero)
            {
                return;
            }

            try
            {
                if (_windowSource == null)
                {
                    _windowSource = HwndSource.FromHwnd(_windowHandle);
                    _windowSource?.AddHook(WindowProc);
                }

                ApplyNoActivateStyle();
            }
            catch (Exception ex)
            {
                Log.Debug("[VIDEO PLAYER] Failed to initialize display-only window mode: {Message}", ex.Message);
            }
        }

        private void ApplyNoActivateStyle()
        {
            if (_windowHandle == IntPtr.Zero)
            {
                return;
            }

            try
            {
                var currentStyle = GetWindowLongPtr(_windowHandle, GWL_EXSTYLE);
                var desiredStyle = new IntPtr(currentStyle.ToInt64() | WS_EX_NOACTIVATE);
                if (desiredStyle != currentStyle)
                {
                    SetWindowLongPtr(_windowHandle, GWL_EXSTYLE, desiredStyle);
                    Log.Debug("[VIDEO PLAYER] Applied WS_EX_NOACTIVATE to video window");
                }
            }
            catch (Exception ex)
            {
                Log.Debug("[VIDEO PLAYER] Unable to apply no-activate style: {Message}", ex.Message);
            }
        }

        private void RestoreControllerFocus()
        {
            if (_controllerWindowHandle == IntPtr.Zero || !IsWindow(_controllerWindowHandle))
            {
                CaptureControllerWindowHandle();
            }

            if (_controllerWindowHandle == IntPtr.Zero || !IsWindow(_controllerWindowHandle))
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (!SetForegroundWindow(_controllerWindowHandle))
                    {
                        Log.Debug("[VIDEO PLAYER] SetForegroundWindow rejected for handle {Handle}", _controllerWindowHandle);
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug("[VIDEO PLAYER] Unable to restore controller focus: {Message}", ex.Message);
                }
            }), DispatcherPriority.Background);
        }

        private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case WM_MOUSEACTIVATE:
                    handled = true;
                    return new IntPtr(MA_NOACTIVATE);
                case WM_ACTIVATE:
                    if ((wParam.ToInt64() & 0xFFFF) != WA_INACTIVE)
                    {
                        RestoreControllerFocus();
                        handled = true;
                        return IntPtr.Zero;
                    }
                    break;
                case WM_SETFOCUS:
                    RestoreControllerFocus();
                    handled = true;
                    return IntPtr.Zero;
                case WM_NCACTIVATE:
                    if (wParam != IntPtr.Zero)
                    {
                        handled = true;
                        return IntPtr.Zero;
                    }
                    break;
            }

            return IntPtr.Zero;
        }

        public void SetBassGain(float gain)
        {
            try
            {
                if (MediaPlayer == null) return;
                _equalizer ??= new Equalizer();
                // Boost low-frequency bands
                _equalizer.SetAmp(gain, 0);
                _equalizer.SetAmp(gain, 1);
                MediaPlayer.SetEqualizer(_equalizer);
                Log.Information("[VIDEO PLAYER] Bass gain set to {Gain}dB", gain);
            }
            catch (Exception ex)
            {
                Log.Error("[VIDEO PLAYER] Failed to set bass gain: {Message}", ex.Message);
            }
        }

        private string[] BuildMediaOptions(string? videoDevice, string? pluginDirectory, bool useHardwareDecoding)
        {
            var options = new List<string>();

            AddOptionIfPresent(options, CreateLibVlcOption("--directx-device", videoDevice));
            AddOptionIfPresent(options, CreateLibVlcOption("--plugin-path", pluginDirectory));
            options.Add("--no-video-title-show");
            options.Add("--no-osd");
            options.Add("--no-video-deco");
            AddOptionIfPresent(options, CreateLibVlcOption("--avcodec-hw", useHardwareDecoding ? "any" : "none"));

            return options.ToArray();
        }

        public VideoPlayerWindow()
        {
            try
            {
                Log.Information("[VIDEO PLAYER] Initializing video player window");
                _libVlcPluginDirectory = ResolveLibVlcPluginDirectory();
                if (string.IsNullOrWhiteSpace(_libVlcPluginDirectory) || !Directory.Exists(_libVlcPluginDirectory))
                {
                    Log.Warning("[VIDEO PLAYER] LibVLC plugins folder not found. Using LibVLC defaults. Checked path: {PluginDir}",
                        _libVlcPluginDirectory ?? "<none>");
                }

                Core.Initialize();

                var baseOptions = new List<string>
                {
                    "--no-video-title-show",
                    "--no-osd",
                    "--no-video-deco"
                };
                AddOptionIfPresent(baseOptions, CreateLibVlcOption("--plugin-path", _libVlcPluginDirectory));

                try
                {
                    var hardwareOptions = new List<string>(baseOptions)
                    {
                        CreateLibVlcOption("--avcodec-hw", "any")!
                    };
                    _libVLC = new LibVLC(hardwareOptions.ToArray());
                    Log.Information("[VIDEO PLAYER] LibVLC initialized with hardware decoding, Version: {Version}, PluginPath: {PluginDir}",
                        _libVLC.Version, _libVlcPluginDirectory ?? "<default>");
                }
                catch (Exception ex)
                {
                    Log.Warning("[VIDEO PLAYER] Failed to initialize LibVLC with hardware decoding: {Message}. Falling back to software decoding.", ex.Message);
                    var softwareOptions = new List<string>(baseOptions)
                    {
                        CreateLibVlcOption("--avcodec-hw", "none")!
                    };
                    _libVLC = new LibVLC(softwareOptions.ToArray());
                    Log.Information("[VIDEO PLAYER] LibVLC initialized with software decoding, Version: {Version}, PluginPath: {PluginDir}",
                        _libVLC.Version, _libVlcPluginDirectory ?? "<default>");
                }

                ShowInTaskbar = true;
                Owner = null;
                WindowStartupLocation = WindowStartupLocation.Manual;
                CaptureControllerWindowHandle();
                InitializeComponent();
                RefreshEdgeGradients();
                _overlayVm.IsBlueState = true;
                ShowIdleScreen();
                SyncVideoSurfaceSize();

                InitializeMediaPlayer();
                SourceInitialized += VideoPlayerWindow_SourceInitialized;
                Loaded += VideoPlayerWindow_Loaded;
                SizeChanged += VideoPlayerWindow_SizeChanged;
                _settingsService.AudioDeviceChanged += SettingsService_AudioDeviceChanged;
                _settingsService.VideoDeviceChanged += SettingsService_VideoDeviceChanged;
                SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
                Log.Information("[VIDEO PLAYER] Video player window initialized successfully");
            }
            catch (Exception ex)
            {
                Log.Error("[VIDEO PLAYER] Failed to initialize video player window: {Message}. Ensure libvlc.dll, libvlccore.dll, and plugins folder are available.", ex.Message);
                MessageBox.Show($"Failed to initialize video player: {ex.Message}. Ensure libvlc.dll, libvlccore.dll, and plugins folder are accessible.", "Error", MessageBoxButton.OK, MessageBoxImage.None);
                Close();
            }
        }

        public void ShowWindow()
        {
            Log.Information("[VIDEO PLAYER] Preparing to show window: ShowActivated={ShowActivated}, IsVisible={IsVisible}, State={WindowState}",
                ShowActivated, IsVisible, WindowState);
            WindowStyle = WindowStyle.None;
            EnsureShownBeforeMaximize();
            SetDisplayDevice();
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
                Log.Information("[VIDEO PLAYER] Shown Normal; deferring maximize");
            }
            else if (WindowState != WindowState.Normal)
            {
                WindowState = WindowState.Normal;
                Log.Information("[VIDEO PLAYER] Window made normal before maximize; current state={State}", WindowState);
            }
            else
            {
                Log.Information("[VIDEO PLAYER] Window already visible; deferring maximize");
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
                    Log.Information("[VIDEO PLAYER] Maximized on idle; final WindowState={WindowState}, ShowActivated={ShowActivated}",
                        WindowState, ShowActivated);
                }
            }), DispatcherPriority.Render);
        }

        private void InitializeMediaPlayer()
        {
            try
            {
                if (_libVLC == null)
                {
                    throw new InvalidOperationException("LibVLC is not initialized");
                }

                if (MediaPlayer != null)
                {
                    MediaPlayer.PositionChanged -= MediaPlayer_PositionChanged;
                    MediaPlayer.EndReached -= MediaPlayer_EndReached;
                    MediaPlayer.EncounteredError -= MediaPlayer_EncounteredError;
                    MediaPlayer.LengthChanged -= MediaPlayer_LengthChanged;
                    MediaPlayer.Playing -= MediaPlayer_Playing;
                    MediaPlayer.Paused -= MediaPlayer_Paused;
                    MediaPlayer.Stopped -= MediaPlayer_Stopped;
                    MediaPlayer.Dispose();
                    Log.Information("[VIDEO PLAYER] Disposed previous MediaPlayer instance before reinitialization");
                }

                MediaPlayer = new LibVLCSharp.Shared.MediaPlayer(_libVLC);
                MediaPlayer.EndReached += MediaPlayer_EndReached;
                MediaPlayer.PositionChanged += MediaPlayer_PositionChanged;
                MediaPlayer.EncounteredError += MediaPlayer_EncounteredError;
                MediaPlayer.LengthChanged += MediaPlayer_LengthChanged;
                MediaPlayer.Playing += MediaPlayer_Playing;
                MediaPlayer.Paused += MediaPlayer_Paused;
                MediaPlayer.Stopped += MediaPlayer_Stopped;
                VideoPlayer.MediaPlayer = MediaPlayer;

                if (_equalizer != null)
                {
                    MediaPlayer.SetEqualizer(_equalizer);
                }

                ApplyAudioOutputSelection();

                MediaPlayerReinitialized?.Invoke(this, EventArgs.Empty);
                Log.Information("[VIDEO PLAYER] MediaPlayer initialized/reinitialized successfully");
            }
            catch (Exception ex)
            {
                Log.Error("[VIDEO PLAYER] Failed to initialize MediaPlayer: {Message}", ex.Message);
                throw;
            }
        }

        private void MediaPlayer_PositionChanged(object? sender, MediaPlayerPositionChangedEventArgs e)
        {
            PositionChanged?.Invoke(this, e);
        }

        private void MediaPlayer_Playing(object? sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                Log.Information("[VIDEO PLAYER] MediaPlayer entered playing state");
                ShowVideoSurface();
                SyncVideoSurfaceSize();
            });
        }

        private void MediaPlayer_Paused(object? sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                Log.Information("[VIDEO PLAYER] MediaPlayer entered paused state");
                ShowBrandScreen();
                ApplyNoActivateStyle();
                RestoreControllerFocus();
            });
        }

        private void MediaPlayer_Stopped(object? sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                var isPlaying = MediaPlayer?.IsPlaying ?? false;
                Log.Information("[VIDEO PLAYER] MediaPlayer stopped playback; IsPlaying={IsPlaying}; ShowBrandOnStop={ShowBrand}",
                    isPlaying,
                    _showBrandOnStopEvent);

                if (_showBrandOnStopEvent && !isPlaying)
                {
                    ShowBrandScreen();
                }
                else
                {
                    Log.Information("[VIDEO PLAYER] Stop event ignored because a new playback session is active");
                }
            });
        }

        private (string Module, string DeviceId, string? Description)? ApplyAudioOutputSelection()
        {
            if (_libVLC == null || MediaPlayer == null)
            {
                return null;
            }

            lock (_audioSelectionLock)
            {
                try
                {
                    var desiredModule = _settingsService.Settings.AudioOutputModule ?? "mmdevice";
                    var availableModules = GetAvailableAudioOutputModules();
                    var module = desiredModule;

                    if (availableModules.Count > 0)
                    {
                        if (!availableModules.Any(o => string.Equals(o.Name, module, StringComparison.OrdinalIgnoreCase)))
                        {
                            Log.Warning("[AUDIO] Output module {Module} not found, defaulting to mmdevice", module);
                            module = "mmdevice";
                        }

                        if (!availableModules.Any(o => string.Equals(o.Name, module, StringComparison.OrdinalIgnoreCase)))
                        {
                            var fallback = availableModules.FirstOrDefault(o => !string.IsNullOrWhiteSpace(o.Name));
                            if (!string.IsNullOrWhiteSpace(fallback.Name))
                            {
                                Log.Warning("[AUDIO] Falling back to first discovered audio module {Module}", fallback.Name);
                                module = fallback.Name!;
                            }
                        }
                    }
                    else if (!string.Equals(module, "mmdevice", StringComparison.OrdinalIgnoreCase))
                    {
                        Log.Warning("[AUDIO] No audio output modules discovered, falling back to mmdevice");
                        module = "mmdevice";
                    }

                    if (string.IsNullOrWhiteSpace(module))
                    {
                        Log.Warning("[AUDIO] Unable to determine an audio output module to apply");
                        return null;
                    }

                    if (!TrySetAudioOutputModule(module))
                    {
                        if (!string.Equals(module, "mmdevice", StringComparison.OrdinalIgnoreCase) && TrySetAudioOutputModule("mmdevice"))
                        {
                            module = "mmdevice";
                        }
                        else
                        {
                            Log.Warning("[AUDIO] Failed to apply any audio output module");
                            return null;
                        }
                    }

                    var resolved = ResolveDeviceId(module, _settingsService.Settings.AudioOutputDeviceId);
                    var deviceId = resolved.DeviceId;
                    var description = resolved.Description;
                    if (!string.IsNullOrWhiteSpace(deviceId))
                    {
                        MediaPlayer.SetOutputDevice(module, deviceId);
                        PersistAudioSelection(module, deviceId);
                        Log.Information("[AUDIO] Set output: Module={Module}, DeviceId={DeviceId}", module, deviceId);
                        return (module, deviceId, description);
                    }

                    Log.Warning("[AUDIO] No audio output devices found for module {Module}", module);
                }
                catch (Exception ex)
                {
                    Log.Error("[AUDIO] Failed to apply audio output selection: {Message}", ex.Message);
                }
            }

            return null;
        }

        private IReadOnlyList<(string Name, string? Description)> GetAvailableAudioOutputModules()
        {
            var modules = new List<(string Name, string? Description)>();

            if (_libVLC == null)
            {
                return modules;
            }

            object? enumeration = null;

            try
            {
                var libVlcType = typeof(LibVLC);
                var property = libVlcType.GetProperty("AudioOutputs");
                if (property != null)
                {
                    enumeration = property.GetValue(_libVLC);
                }

                if (enumeration == null)
                {
                    var method = libVlcType.GetMethod("AudioOutputList", Type.EmptyTypes);
                    if (method != null)
                    {
                        enumeration = method.Invoke(_libVLC, Array.Empty<object?>());
                    }
                }

                if (enumeration is System.Collections.IEnumerable enumerable)
                {
                    try
                    {
                        foreach (var entry in enumerable)
                        {
                            if (entry == null)
                            {
                                continue;
                            }

                            var name = entry.GetType().GetProperty("Name")?.GetValue(entry) as string;
                            if (string.IsNullOrWhiteSpace(name))
                            {
                                continue;
                            }

                            var description = entry.GetType().GetProperty("Description")?.GetValue(entry) as string;
                            modules.Add((name, description));
                        }
                    }
                    finally
                    {
                        if (enumeration is IDisposable disposable)
                        {
                            disposable.Dispose();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[AUDIO] Failed to enumerate audio outputs: {Message}", ex.Message);
            }

            return modules;
        }

        private bool TrySetAudioOutputModule(string module)
        {
            if (MediaPlayer == null)
            {
                return false;
            }

            try
            {
                var method = typeof(LibVLCSharp.Shared.MediaPlayer).GetMethod("SetAudioOutput", new[] { typeof(string) });
                if (method != null)
                {
                    var result = method.Invoke(MediaPlayer, new object?[] { module });
                    if (result is bool boolResult)
                    {
                        return boolResult;
                    }

                    return true;
                }

                var property = typeof(LibVLCSharp.Shared.MediaPlayer).GetProperty("AudioOutput");
                if (property != null && property.CanWrite)
                {
                    property.SetValue(MediaPlayer, module);
                    return true;
                }

                Log.Warning("[AUDIO] No API available to set audio output module to {Module}", module);
                return false;
            }
            catch (Exception ex)
            {
                Log.Warning("[AUDIO] Failed to set audio output module {Module}: {Message}", module, ex.Message);
                return false;
            }
        }

        private void MediaPlayer_LengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e)
        {
            try
            {
                Log.Information("[VIDEO PLAYER] Media length reported: {Length}ms", e.Length);
                MediaLengthChanged?.Invoke(this, e.Length);
            }
            catch (Exception ex)
            {
                Log.Error("[VIDEO PLAYER] Failed to propagate length change: {Message}", ex.Message);
            }
        }

        private void MediaPlayer_EncounteredError(object? sender, EventArgs e)
        {
            Log.Error("[VIDEO PLAYER] VLC encountered an error during playback");
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (TryRecoverFromPlaybackError())
                    {
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("[VIDEO PLAYER] Failed during playback recovery attempt: {Message}", ex.Message);
                }

                StopVideo();
                MessageBox.Show("Playback error occurred in VLC. If Windows Volume Mixer shows this app muted or routed to another endpoint, unmute/select the X32. Also check if another application is using the device in exclusive mode.", "Error", MessageBoxButton.OK, MessageBoxImage.None);
            });
        }

        private bool TryRecoverFromPlaybackError()
        {
            if (string.IsNullOrWhiteSpace(_currentVideoPath))
            {
                Log.Warning("[VIDEO PLAYER] No active video path available for recovery");
                return false;
            }

            if (!File.Exists(_currentVideoPath))
            {
                Log.Warning("[VIDEO PLAYER] Active video path missing on disk during recovery: {Path}", _currentVideoPath);
                return false;
            }

            if (!_lastPlaybackUsedHardwareDecoding)
            {
                Log.Information("[VIDEO PLAYER] Playback error occurred without hardware decoding; skipping automatic recovery");
                return false;
            }

            if (_hasTriedSoftwareFallbackForCurrentMedia)
            {
                Log.Warning("[VIDEO PLAYER] Software decoding fallback already attempted for {Path}", _currentVideoPath);
                return false;
            }

            _hasTriedSoftwareFallbackForCurrentMedia = true;

            try
            {
                Log.Warning("[VIDEO PLAYER] Attempting software decoding fallback after hardware playback error");
                try
                {
                    MediaPlayer?.Stop();
                }
                catch (Exception ex)
                {
                    Log.Warning("[VIDEO PLAYER] Failed to stop media player before fallback: {Message}", ex.Message);
                }

                DisposeCurrentMedia();

                if (TryStartPlaybackWithRetries(_currentVideoPath, isDiagnostic: false, preferHardware: false))
                {
                    Log.Information("[VIDEO PLAYER] Software decoding fallback succeeded for {Path}", _currentVideoPath);
                    return true;
                }

                Log.Error("[VIDEO PLAYER] Software decoding fallback failed for {Path}", _currentVideoPath);
                return false;
            }
            finally
            {
                _lastPlaybackUsedHardwareDecoding = false;
            }
        }

        private void VideoPlayerWindow_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            SyncVideoSurfaceSize();
        }

        private void MediaPlayer_EndReached(object? sender, EventArgs e)
        {
            bool suppressed = _suppressSongEnded;
            StopVideo();
            if (suppressed)
            {
                return;
            }

            SongEnded?.Invoke(this, EventArgs.Empty);
        }

        private void SettingsService_AudioDeviceChanged(object? sender, string deviceId)
        {
            try
            {
                Log.Information("[VIDEO PLAYER] Audio device changed: {DeviceId}", deviceId);
                if (!string.IsNullOrEmpty(_currentVideoPath) && _libVLC != null)
                {
                    _currentPosition = MediaPlayer?.Time ?? 0;
                    bool wasPlaying = MediaPlayer?.IsPlaying ?? false;
                    MediaPlayer?.Stop();
                    DisposeCurrentMedia();
                    InitializeMediaPlayer();
                    ApplyAudioOutputSelection();
                    VideoPlayer.Visibility = Visibility.Visible;
                    VideoPlayer.Opacity = 1;

                    if (File.Exists(_currentVideoPath) && TryStartPlaybackWithRetries(_currentVideoPath, false))
                    {
                        if (_currentPosition > 0)
                        {
                            MediaPlayer!.Time = _currentPosition;
                        }

                        if (!wasPlaying)
                        {
                            MediaPlayer!.Pause();
                        }

                        Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            Log.Information("[VIDEO PLAYER] Preparing to show window: ShowActivated={ShowActivated}, IsVisible={IsVisible}, State={WindowState}",
                                ShowActivated, IsVisible, WindowState);
                            WindowStyle = WindowStyle.None;
                            EnsureShownBeforeMaximize();
                            SetDisplayDevice();
                        });
                        Log.Information("[VIDEO PLAYER] Switched audio device, resumed at position: {Position}ms", _currentPosition);
                    }
                    else
                    {
                        Log.Error("[VIDEO PLAYER] Failed to resume after audio device change");
                        MessageBox.Show("Failed to switch audio device. Check for exclusive access or muted output and try again.", "Audio", MessageBoxButton.OK, MessageBoxImage.None);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("[VIDEO PLAYER] Failed to switch audio device: {Message}", ex.Message);
                MessageBox.Show($"Failed to switch audio device: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.None);
            }
        }

        private void SettingsService_VideoDeviceChanged(object? sender, string deviceId)
        {
            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    Log.Information("[VIDEO PLAYER] Video device preference changed to {Device}. Repositioning window.", deviceId);
                    SetDisplayDevice();
                }
                catch (Exception ex)
                {
                    Log.Error("[VIDEO PLAYER] Failed to apply video device change: {Message}", ex.Message);
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
                    Log.Information("[VIDEO PLAYER] Display configuration changed. Verifying program output display.");
                    SetDisplayDevice();
                }
                catch (Exception ex)
                {
                    Log.Error("[VIDEO PLAYER] Failed to adjust after display change: {Message}", ex.Message);
                }
            });
        }

        private void VideoPlayerWindow_SourceInitialized(object? sender, EventArgs e)
        {
            try
            {
                Log.Information("[VIDEO PLAYER] Source initialized, setting display");
                _windowHandle = new WindowInteropHelper(this).Handle;
                InitializeDisplayOnlyWindow();
                Log.Information("[VIDEO PLAYER] Window handle captured: {Handle}", _windowHandle);
            }
            catch (Exception ex)
            {
                Log.Error("[VIDEO PLAYER] Failed to set display on source initialized: {Message}", ex.Message);
            }
        }

        private void VideoPlayerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Log.Information("[VIDEO PLAYER] Video player window loaded, finalizing display");
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                Log.Information("[VIDEO PLAYER] Preparing to show window: ShowActivated={ShowActivated}, IsVisible={IsVisible}, State={WindowState}",
                    ShowActivated, IsVisible, WindowState);
                EnsureShownBeforeMaximize();
                SetDisplayDevice();

                Visibility = Visibility.Visible;
                VideoPlayer.Visibility = Visibility.Collapsed;
                VideoPlayer.Opacity = 0;
                ShowActivated = false;
                ApplyNoActivateStyle();
                RestoreControllerFocus();

                _hideVideoViewTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(3),
                    IsEnabled = true
                };
                _hideVideoViewTimer.Tick += (s, args) =>
                {
                    ShowIdleScreen();
                    _hideVideoViewTimer.Stop();
                    Log.Information("[VIDEO PLAYER] VideoView hidden after initial delay");
                };
                _hideVideoViewTimer.Start();

                var hwnd = new WindowInteropHelper(this).Handle;
                var currentScreen = System.Windows.Forms.Screen.FromHandle(hwnd);
                if (currentScreen.DeviceName != _settingsService.Settings.KaraokeVideoDevice)
                {
                    Log.Warning("[VIDEO PLAYER] Incorrect screen: {Screen}, repositioning to {Target}", currentScreen.DeviceName, _settingsService.Settings.KaraokeVideoDevice);
                    SetDisplayDevice();
                }

                Log.Information("[VIDEO PLAYER] Current screen: {DeviceName}, Bounds: {Left}x{Top} {Width}x{Height}, Primary: {Primary}",
                    currentScreen.DeviceName, currentScreen.Bounds.Left, currentScreen.Bounds.Top, currentScreen.Bounds.Width, currentScreen.Bounds.Height, currentScreen.Primary);
                Log.Information("[VIDEO PLAYER] Final window bounds: Left={Left}, Top={Top}, Width={Width}, Height={Height}",
                    Left, Top, Width, Height);
                SyncVideoSurfaceSize();
                UpdateLayout();
                Log.Information("[VIDEO PLAYER] VideoView bounds after maximize: Width={Width}, Height={Height}, ActualWidth={ActualWidth}, ActualHeight={ActualHeight}",
                    VideoPlayer.Width, VideoPlayer.Height, VideoPlayer.ActualWidth, VideoPlayer.ActualHeight);
                Log.Information("[VIDEO PLAYER] Window visibility: {Visibility}, ShowInTaskbar: {ShowInTaskbar}", Visibility, ShowInTaskbar);
            }
            catch (Exception ex)
            {
                Log.Error("[VIDEO PLAYER] Failed to set display on load: {Message}", ex.Message);
                MessageBox.Show($"Failed to set display: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.None);
            }
        }

        public void PlayVideo(string videoPath, bool isDiagnostic = false)
        {
            bool playbackStarted = false;
            bool previousSuppress = _suppressSongEnded;

            try
            {
                Log.Information("[VIDEO PLAYER] Attempting to play media: {VideoPath}", videoPath);
                if (_libVLC == null)
                {
                    throw new InvalidOperationException("Media player not initialized");
                }

                if (MediaPlayer == null)
                {
                    InitializeMediaPlayer();
                }

                // For diagnostic playback (e.g., Test Tone), ensure we are not muted and have a sane volume,
                // and avoid any previously persisted device routing that could be invalid.
                if (isDiagnostic && MediaPlayer != null)
                {
                    try
                    {
                        MediaPlayer.Mute = false;
                        if (MediaPlayer.Volume <= 0)
                        {
                            MediaPlayer.Volume = 90;
                        }
                    }
                    catch { /* best-effort */ }
                }

                if (!File.Exists(videoPath))
                {
                    throw new FileNotFoundException($"Video file not found: {videoPath}");
                }

                string? previousPath = _currentVideoPath;
                _suppressSongEnded = isDiagnostic;

                if (!isDiagnostic)
                {
                    _hasTriedSoftwareFallbackForCurrentMedia = false;
                }

                if (!isDiagnostic)
                {
                    _showBrandOnStopEvent = false;
                    _currentVideoPath = videoPath;
                }

                if (!isDiagnostic && _hideVideoViewTimer != null)
                {
                    _hideVideoViewTimer.Stop();
                    Log.Information("[VIDEO PLAYER] Stopped hide timer for playback");
                }

                if (!isDiagnostic)
                {
                    ShowVideoSurface();
                    SyncVideoSurfaceSize();
                }

                bool canResume = !isDiagnostic
                    && MediaPlayer != null
                    && MediaPlayer.State == VLCState.Paused
                    && !string.IsNullOrWhiteSpace(previousPath)
                    && string.Equals(previousPath, videoPath, StringComparison.OrdinalIgnoreCase);

                if (canResume)
                {
                    MediaPlayer!.Play();
                    playbackStarted = true;
                    Log.Information("[VIDEO PLAYER] Resuming video from paused state");
                    return;
                }

                _lastPlaybackUsedHardwareDecoding = false;

                if (!TryStartPlaybackWithRetries(videoPath, isDiagnostic))
                {
                    throw new InvalidOperationException("LibVLC failed to start playback");
                }

                playbackStarted = true;

                if (!isDiagnostic)
                {
                    Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            Log.Information("[VIDEO PLAYER] Preparing to show window: ShowActivated={ShowActivated}, IsVisible={IsVisible}, State={WindowState}",
                                ShowActivated, IsVisible, WindowState);
                            WindowStyle = WindowStyle.None;
                            EnsureShownBeforeMaximize();
                            SetDisplayDevice();
                            var hwnd = new WindowInteropHelper(this).Handle;
                            var currentScreen = System.Windows.Forms.Screen.FromHandle(hwnd);
                            Log.Information("[VIDEO PLAYER] VLC state: IsPlaying={IsPlaying}, State={State}, Fullscreen={Fullscreen}",
                                MediaPlayer?.IsPlaying ?? false, MediaPlayer?.State ?? VLCState.NothingSpecial, MediaPlayer?.Fullscreen ?? false);
                            Log.Information("[VIDEO PLAYER] Window bounds after play: Left={Left}, Top={Top}, Width={Width}, Height={Height}, Screen={Screen}",
                                Left, Top, Width, Height, currentScreen.DeviceName);
                            Log.Information("[VIDEO PLAYER] WindowStyle={WindowStyle}, ShowInTaskbar={ShowInTaskbar}", WindowStyle, ShowInTaskbar);
                            ApplyNoActivateStyle();
                            RestoreControllerFocus();
                        }
                        catch (Exception ex)
                        {
                            Log.Error("[VIDEO PLAYER] Failed to update UI after play: {Message}", ex.Message);
                        }
                    });
                }
                else
                {
                    Log.Information("[AUDIO] Diagnostic playback started for {Path}", videoPath);
                }
            }
            catch (Exception ex)
            {
                Log.Error("[VIDEO PLAYER] Failed to play video: {VideoPath}, Message: {Message}", videoPath, ex.Message);
                if (isDiagnostic)
                {
                    Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show($"Failed to play test tone: {ex.Message}\nVerify the selected audio device and exclusive-mode settings.", "Audio", MessageBoxButton.OK, MessageBoxImage.None);
                    });
                }
                else
                {
                    Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show($"Failed to play video: {ex.Message}\nIf Windows Volume Mixer shows this app muted or routed to another endpoint, unmute/select the X32. Also check if any other app is holding exclusive access.", "Error", MessageBoxButton.OK, MessageBoxImage.None);
                    });
                }
            }
            finally
            {
                if (isDiagnostic && !playbackStarted)
                {
                    _suppressSongEnded = previousSuppress;
                }
                else if (!playbackStarted)
                {
                    _showBrandOnStopEvent = true;
                }
            }
        }

        public void RestartAudioEngine()
        {
            try
            {
                Log.Information("[AUDIO] Restarting audio engine");

                bool wasPlaying = MediaPlayer?.IsPlaying ?? false;
                long resumePosition = MediaPlayer?.Time ?? 0;
                string? path = _currentVideoPath;
                bool previousSuppress = _suppressSongEnded;

                try
                {
                    MediaPlayer?.Stop();
                }
                catch (Exception ex)
                {
                    Log.Verbose("[AUDIO] Stop before restart ignored: {Message}", ex.Message);
                }

                DisposeCurrentMedia();

                if (MediaPlayer != null)
                {
                    MediaPlayer.EndReached -= MediaPlayer_EndReached;
                    MediaPlayer.PositionChanged -= MediaPlayer_PositionChanged;
                    MediaPlayer.EncounteredError -= MediaPlayer_EncounteredError;
                    MediaPlayer.LengthChanged -= MediaPlayer_LengthChanged;
                    MediaPlayer.Playing -= MediaPlayer_Playing;
                    MediaPlayer.Paused -= MediaPlayer_Paused;
                    MediaPlayer.Stopped -= MediaPlayer_Stopped;
                    MediaPlayer.Dispose();
                    MediaPlayer = null;
                }

                _libVLC?.Dispose();

                var pluginDirectory = _libVlcPluginDirectory;
                if (string.IsNullOrWhiteSpace(pluginDirectory) || !Directory.Exists(pluginDirectory))
                {
                    pluginDirectory = ResolveLibVlcPluginDirectory();
                }
                if (string.IsNullOrWhiteSpace(pluginDirectory) || !Directory.Exists(pluginDirectory))
                {
                    Log.Warning("[AUDIO] LibVLC plugins directory unavailable during restart. Using default search path. Checked path: {PluginDir}",
                        pluginDirectory ?? "<none>");
                    pluginDirectory = null;
                }

                var restartBaseOptions = new List<string>
                {
                    "--no-video-title-show",
                    "--no-osd",
                    "--no-video-deco"
                };
                AddOptionIfPresent(restartBaseOptions, CreateLibVlcOption("--plugin-path", pluginDirectory));

                try
                {
                    var hardwareOptions = new List<string>(restartBaseOptions)
                    {
                        CreateLibVlcOption("--avcodec-hw", "any")!
                    };
                    _libVLC = new LibVLC(hardwareOptions.ToArray());
                }
                catch (Exception ex)
                {
                    Log.Warning("[AUDIO] Restart fallback to software decoding: {Message}", ex.Message);
                    var softwareOptions = new List<string>(restartBaseOptions)
                    {
                        CreateLibVlcOption("--avcodec-hw", "none")!
                    };
                    _libVLC = new LibVLC(softwareOptions.ToArray());
                }
                InitializeMediaPlayer();
                ApplyAudioOutputSelection();
                _suppressSongEnded = previousSuppress;

                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    _currentVideoPath = path;
                    if (TryStartPlaybackWithRetries(path, false))
                    {
                        if (resumePosition > 0)
                        {
                            MediaPlayer!.Time = resumePosition;
                        }

                        if (!wasPlaying && MediaPlayer!.IsPlaying)
                        {
                            MediaPlayer.Pause();
                        }

                        Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            Log.Information("[VIDEO PLAYER] Preparing to show window: ShowActivated={ShowActivated}, IsVisible={IsVisible}, State={WindowState}",
                                ShowActivated, IsVisible, WindowState);
                            WindowStyle = WindowStyle.None;
                            EnsureShownBeforeMaximize();
                            SetDisplayDevice();
                        });
                    }
                    else
                    {
                        Log.Error("[AUDIO] Failed to resume playback after audio engine restart for {Path}", path);
                        MessageBox.Show("Failed to restart playback after rebuilding the audio engine. Check the audio device and try again.", "Audio", MessageBoxButton.OK, MessageBoxImage.None);
                    }
                }

                Log.Information("[AUDIO] Audio engine restarted");
            }
            catch (Exception ex)
            {
                Log.Error("[AUDIO] Restart engine failed: {Message}", ex.Message);
                MessageBox.Show($"Failed to restart audio engine: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.None);
            }
        }

        public void PlayTestTone()
        {
            try
            {
                var tonePath = CreateTestToneFile();
                Log.Information("[AUDIO] Playing test tone from {Path}", tonePath);
                PlayVideo(tonePath, isDiagnostic: true);

                Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(10));
                        if (File.Exists(tonePath))
                        {
                            File.Delete(tonePath);
                        }
                    }
                    catch (Exception cleanupEx)
                    {
                        Log.Verbose("[AUDIO] Test tone cleanup ignored: {Message}", cleanupEx.Message);
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error("[AUDIO] Failed to play test tone: {Message}", ex.Message);
                MessageBox.Show($"Failed to play test tone: {ex.Message}", "Audio", MessageBoxButton.OK, MessageBoxImage.None);
            }
        }

        public void PauseVideo()
        {
            try
            {
                Log.Information("[VIDEO PLAYER] Pausing video");
                if (MediaPlayer != null && MediaPlayer.IsPlaying)
                {
                    MediaPlayer.Pause();
                    ShowBrandScreen();
                    Visibility = Visibility.Visible;
                    Log.Information("[VIDEO PLAYER] Preparing to show window: ShowActivated={ShowActivated}, IsVisible={IsVisible}, State={WindowState}",
                        ShowActivated, IsVisible, WindowState);
                    WindowStyle = WindowStyle.None;
                    EnsureShownBeforeMaximize();
                    SetDisplayDevice();
                    ApplyNoActivateStyle();
                    RestoreControllerFocus();
                    Log.Information("[VIDEO PLAYER] VLC state: IsPlaying={IsPlaying}, State={State}, Fullscreen={Fullscreen}",
                        MediaPlayer.IsPlaying, MediaPlayer.State, MediaPlayer.Fullscreen);
                }
                else
                {
                    Log.Information("[VIDEO PLAYER] No video playing to pause");
                }
            }
            catch (Exception ex)
            {
                Log.Error("[VIDEO PLAYER] Failed to pause video: {Message}", ex.Message);
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Failed to pause video: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.None);
                });
            }
        }

        public void StopVideo()
        {
            try
            {
                Log.Information("[VIDEO PLAYER] Stopping video");
                _showBrandOnStopEvent = true;
                if (MediaPlayer != null && (MediaPlayer.IsPlaying || MediaPlayer.State == VLCState.Paused))
                {
                    MediaPlayer.Stop();
                    DisposeCurrentMedia();
                    ShowIdleScreen();
                    Log.Information("[VIDEO PLAYER] Video stopped, VLC state: IsPlaying={IsPlaying}, State={State}",
                        MediaPlayer.IsPlaying, MediaPlayer.State);
                }
                else
                {
                    ShowIdleScreen();
                    Log.Information("[VIDEO PLAYER] No video playing or paused to stop");
                }
                _currentVideoPath = null;
                _currentPosition = 0;
                _suppressSongEnded = false;
                _lastPlaybackUsedHardwareDecoding = false;
                _hasTriedSoftwareFallbackForCurrentMedia = false;
            }
            catch (Exception ex)
            {
                Log.Error("[VIDEO PLAYER] Failed to stop video: {Message}", ex.Message);
            }
        }

        private void ShowVideoSurface()
        {
            void Apply()
            {
                BlueOverlay.Visibility = Visibility.Collapsed;
                VideoPlayer.Visibility = Visibility.Visible;
                VideoPlayer.Opacity = 1;
                OnPlaybackStarted();
                RefreshEdgeGradients();
                SyncVideoSurfaceSize();
            }

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(Apply);
            }
            else
            {
                Apply();
            }
        }

        private void ShowBrandScreen()
        {
            void Apply()
            {
                BlueOverlay.Visibility = Visibility.Visible;
                VideoPlayer.Visibility = Visibility.Collapsed;
                VideoPlayer.Opacity = 0;
                OnPlaybackIdleOrPaused();
            }

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(Apply);
            }
            else
            {
                Apply();
            }
        }

        private void OnPlaybackStarted()
        {
            _overlayVm.IsBlueState = false;
        }

        private void OnPlaybackIdleOrPaused()
        {
            _overlayVm.IsBlueState = true;
        }

        private void RefreshEdgeGradients()
        {
            void Apply()
            {
                ApplyEdgeBrush(LeftEdgeOverlay, "LeftEdgeBrush");
                ApplyEdgeBrush(RightEdgeOverlay, "RightEdgeBrush");
            }

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(Apply);
            }
            else
            {
                Apply();
            }
        }

        private void ApplyEdgeBrush(Border target, string resourceKey)
        {
            if (target == null)
            {
                return;
            }

            if (TryFindResource(resourceKey) is Brush brush)
            {
                if (brush is Freezable freezable)
                {
                    target.Background = (Brush)freezable.Clone();
                }
                else
                {
                    target.Background = brush;
                }
            }
        }

        private const double VideoEdgeOverlap = 2.0;

        private void SyncVideoSurfaceSize()
        {
            void Apply()
            {
                double hostWidth = VideoHost.ActualWidth;
                double hostHeight = VideoHost.ActualHeight;

                if (hostWidth <= 0)
                {
                    hostWidth = ActualWidth;
                }

                if (hostHeight <= 0)
                {
                    hostHeight = ActualHeight;
                }

                VideoPlayer.Margin = new Thickness(-VideoEdgeOverlap, 0, -VideoEdgeOverlap, 0);
                VideoPlayer.Width = hostWidth > 0 ? hostWidth : ActualWidth;
                VideoPlayer.Height = hostHeight > 0 ? hostHeight : ActualHeight;

                VideoPlayer.UpdateLayout();
                VideoPlayer.InvalidateVisual();
                VideoHost.InvalidateVisual();
            }

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(Apply);
            }
            else
            {
                Apply();
            }
        }

        public void ShowIdleScreen()
        {
            try
            {
                void Apply()
                {
                    ShowBrandScreen();
                    ApplyNoActivateStyle();
                    RestoreControllerFocus();
                }

                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.Invoke(Apply);
                }
                else
                {
                    Apply();
                }
            }
            catch (Exception ex)
            {
                Log.Error("[VIDEO PLAYER] Failed to show idle screen: {Message}", ex.Message);
            }
        }

        public void RestartVideo()
        {
            try
            {
                Log.Information("[VIDEO PLAYER] Restarting video");
                if (_libVLC == null)
                {
                    Log.Warning("[VIDEO PLAYER] Restart requested before LibVLC initialization completed");
                    return;
                }

                if (string.IsNullOrWhiteSpace(_currentVideoPath) || !File.Exists(_currentVideoPath))
                {
                    Log.Warning("[VIDEO PLAYER] Restart requested but no active media is cached: {Path}", _currentVideoPath ?? "<null>");
                    return;
                }

                var previousVolume = MediaPlayer?.Volume;
                bool wasMuted = MediaPlayer?.Mute ?? false;

                MediaPlayer?.Stop();
                DisposeCurrentMedia();
                InitializeMediaPlayer();
                ApplyAudioOutputSelection();

                if (!TryStartPlaybackWithRetries(_currentVideoPath, false))
                {
                    throw new InvalidOperationException("LibVLC failed to start playback during restart");
                }

                MediaPlayer!.Time = 0;
                if (wasMuted)
                {
                    MediaPlayer.Mute = true;
                }
                else if (previousVolume.HasValue)
                {
                    MediaPlayer.Volume = previousVolume.Value;
                }

                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    Log.Information("[VIDEO PLAYER] Preparing to show window: ShowActivated={ShowActivated}, IsVisible={IsVisible}, State={WindowState}",
                        ShowActivated, IsVisible, WindowState);
                    VideoPlayer.Visibility = Visibility.Visible;
                    VideoPlayer.Opacity = 1;
                    Visibility = Visibility.Visible;
                    WindowStyle = WindowStyle.None;
                    EnsureShownBeforeMaximize();
                    SetDisplayDevice();
                    ApplyNoActivateStyle();
                    RestoreControllerFocus();
                });

                Log.Information("[VIDEO PLAYER] Video restarted with new LibVLC media session, VLC state: IsPlaying={IsPlaying}, State={State}",
                    MediaPlayer.IsPlaying, MediaPlayer.State);
            }
            catch (Exception ex)
            {
                Log.Error("[VIDEO PLAYER] Failed to restart video: {Message}", ex.Message);
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Failed to restart video: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.None);
                });
            }
        }

        public void EndShow()
        {
            try
            {
                Log.Information("[VIDEO PLAYER] Ending show");
                StopVideo();
                VideoPlayer.Visibility = Visibility.Collapsed;
                Close();
                Log.Information("[VIDEO PLAYER] Show ended, window closing");
            }
            catch (Exception ex)
            {
                Log.Error("[VIDEO PLAYER] Failed to end show: {Message}", ex.Message);
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
                    Log.Warning("[VIDEO PLAYER] No displays detected when attempting to position the video window");
                    return;
                }

                var targetScreen = screens.FirstOrDefault(screen =>
                        !string.IsNullOrWhiteSpace(targetDevice) &&
                        screen.DeviceName.Equals(targetDevice, StringComparison.OrdinalIgnoreCase))
                    ?? (hwnd != IntPtr.Zero
                        ? System.Windows.Forms.Screen.FromHandle(hwnd)
                        : System.Windows.Forms.Screen.PrimaryScreen ?? screens.First());

                Left = targetScreen.Bounds.Left;
                Top = targetScreen.Bounds.Top;
                Width = targetScreen.Bounds.Width;
                Height = targetScreen.Bounds.Height;

                Log.Information("[VIDEO PLAYER] Positioned to {Device} -> {Left}x{Top} {Width}x{Height}",
                    targetScreen.DeviceName, Left, Top, Width, Height);
            }
            catch (Exception ex)
            {
                Log.Error("[VIDEO PLAYER] Failed to set display device: {Message}", ex.Message);
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            Log.Information("[VIDEO PLAYER] Window closing");
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                Log.Information("[VIDEO PLAYER] Closing video player window");
                if (_windowSource != null)
                {
                    _windowSource.RemoveHook(WindowProc);
                    _windowSource = null;
                }
                _windowHandle = IntPtr.Zero;
                _hideVideoViewTimer?.Stop();
                if (MediaPlayer != null)
                {
                    if (MediaPlayer.IsPlaying || MediaPlayer.State == VLCState.Paused)
                    {
                        MediaPlayer.Stop();
                    }
                    DisposeCurrentMedia();
                    MediaPlayer.PositionChanged -= MediaPlayer_PositionChanged;
                    MediaPlayer.EndReached -= MediaPlayer_EndReached;
                    MediaPlayer.EncounteredError -= MediaPlayer_EncounteredError;
                    MediaPlayer.LengthChanged -= MediaPlayer_LengthChanged;
                    MediaPlayer.Playing -= MediaPlayer_Playing;
                    MediaPlayer.Paused -= MediaPlayer_Paused;
                    MediaPlayer.Stopped -= MediaPlayer_Stopped;
                    MediaPlayer.Dispose();
                    MediaPlayer = null;
                }
                if (_libVLC != null)
                {
                    _libVLC.Dispose();
                }
                _settingsService.AudioDeviceChanged -= SettingsService_AudioDeviceChanged;
                _settingsService.VideoDeviceChanged -= SettingsService_VideoDeviceChanged;
                SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
                Closed?.Invoke(this, EventArgs.Empty);
                Log.Information("[VIDEO PLAYER] Video player window closed successfully");
            }
            catch (Exception ex)
            {
                Log.Error("[VIDEO PLAYER] Error during cleanup: {Message}", ex.Message);
            }
            base.OnClosed(e);
        }

        private (string? DeviceId, string? Description) ResolveDeviceId(string module, string? desiredDeviceId)
        {
            if (MediaPlayer == null)
            {
                return (desiredDeviceId, null);
            }

            string? resolvedDeviceId = desiredDeviceId;
            string? resolvedDescription = null;

            try
            {
                // LibVLC 3.8 exposes audio device enumeration through the AudioOutputDeviceEnum property.
                // The property returns a disposable sequence filtered by the module currently configured on
                // the media player. Because the API surface has shifted between releases (method vs. property),
                // use reflection-based access to keep compatibility with both forms without introducing direct
                // compile-time dependencies on either shape.
                var enumMember = typeof(LibVLCSharp.Shared.MediaPlayer).GetMember("AudioOutputDeviceEnum").FirstOrDefault();
                if (enumMember is System.Reflection.PropertyInfo propInfo)
                {
                    var enumValue = propInfo.GetValue(MediaPlayer);
                    if (enumValue is IDisposable disposable && enumValue is System.Collections.IEnumerable enumerable)
                    {
                        using (disposable)
                        {
                            foreach (var device in enumerable)
                            {
                                if (device == null)
                                {
                                    continue;
                                }

                                var deviceModule = device.GetType().GetProperty("AudioOutput")?.GetValue(device) as string;
                                if (!string.IsNullOrWhiteSpace(deviceModule) && !string.Equals(deviceModule, module, StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }

                                var deviceId = device.GetType().GetProperty("DeviceId")?.GetValue(device) as string;
                                var deviceDescription = device.GetType().GetProperty("Description")?.GetValue(device) as string;

                                if (!string.IsNullOrWhiteSpace(desiredDeviceId) && string.Equals(deviceId, desiredDeviceId, StringComparison.OrdinalIgnoreCase))
                                {
                                    return (deviceId, deviceDescription);
                                }

                                if (string.Equals(module, "mmdevice", StringComparison.OrdinalIgnoreCase) && deviceDescription?.IndexOf("x32", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    return (deviceId, deviceDescription);
                                }

                                resolvedDescription ??= deviceDescription;
                                resolvedDeviceId ??= deviceId;
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(resolvedDeviceId))
                        {
                            return (resolvedDeviceId, resolvedDescription);
                        }
                    }
                }
                else if (enumMember is System.Reflection.MethodInfo methodInfo)
                {
                    var enumeration = methodInfo.Invoke(MediaPlayer, new object?[] { module });
                    if (enumeration is System.Collections.IEnumerable enumerable)
                    {
                        string? InspectDevice(object device)
                        {
                            var deviceId = device.GetType().GetProperty("DeviceId")?.GetValue(device) as string;
                            var deviceDescription = device.GetType().GetProperty("Description")?.GetValue(device) as string;

                            if (!string.IsNullOrWhiteSpace(desiredDeviceId) && string.Equals(deviceId, desiredDeviceId, StringComparison.OrdinalIgnoreCase))
                            {
                                resolvedDeviceId = deviceId;
                                resolvedDescription = deviceDescription;
                                return deviceId;
                            }

                            if (string.Equals(module, "mmdevice", StringComparison.OrdinalIgnoreCase) && deviceDescription?.IndexOf("x32", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                resolvedDeviceId = deviceId;
                                resolvedDescription = deviceDescription;
                                return deviceId;
                            }

                            resolvedDescription ??= deviceDescription;
                            resolvedDeviceId ??= deviceId;
                            return null;
                        }

                        string? Iterate()
                        {
                            foreach (var device in enumerable)
                            {
                                if (device == null)
                                {
                                    continue;
                                }

                                var result = InspectDevice(device);
                                if (!string.IsNullOrWhiteSpace(result))
                                {
                                    return result;
                                }
                            }

                            return null;
                        }

                        if (enumeration is IDisposable disposable)
                        {
                            using (disposable)
                            {
                                var resolved = Iterate();
                                if (!string.IsNullOrWhiteSpace(resolved))
                                {
                                    return (resolvedDeviceId, resolvedDescription);
                                }
                            }
                        }
                        else
                        {
                            var resolved = Iterate();
                            if (!string.IsNullOrWhiteSpace(resolved))
                            {
                                return (resolvedDeviceId, resolvedDescription);
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(resolvedDeviceId))
                        {
                            return (resolvedDeviceId, resolvedDescription);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[AUDIO] Failed to resolve device id for module {Module}: {Message}", module, ex.Message);
            }

            return (resolvedDeviceId, resolvedDescription);
        }

        private void PersistAudioSelection(string module, string deviceId)
        {
            try
            {
                bool changed = false;
                if (!string.Equals(_settingsService.Settings.AudioOutputModule, module, StringComparison.OrdinalIgnoreCase))
                {
                    _settingsService.Settings.AudioOutputModule = module;
                    changed = true;
                }

                if (!string.Equals(_settingsService.Settings.AudioOutputDeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
                {
                    _settingsService.Settings.AudioOutputDeviceId = deviceId;
                    changed = true;
                }

                if (changed)
                {
                    _settingsService.Save();
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[AUDIO] Failed to persist audio selection: {Message}", ex.Message);
            }
        }

        private void LogAudioAttempt(string module, string? deviceId, string? description, bool useHardware)
        {
            try
            {
                var volume = MediaPlayer?.Volume ?? -1;
                var mute = MediaPlayer?.Mute ?? false;
                Log.Information(
                    "[AUDIO] Using Module={Module} DeviceId={DeviceId} Desc={Description} Volume={Volume} Mute={Mute} HW={HW}",
                    module,
                    string.IsNullOrWhiteSpace(deviceId) ? "<default>" : deviceId,
                    string.IsNullOrWhiteSpace(description) ? "<unknown>" : description,
                    volume,
                    mute,
                    useHardware);
            }
            catch (Exception ex)
            {
                Log.Warning("[AUDIO] Failed to log audio attempt: {Message}", ex.Message);
            }
        }

        private bool TryPlayWithBackend(string module, string? deviceId, string mediaPath, bool useHardwareDecoding)
        {
            if (_libVLC == null || MediaPlayer == null)
            {
                return false;
            }

            try
            {
                var resolved = ResolveDeviceId(module, deviceId);
                var resolvedDeviceId = resolved.DeviceId;
                var description = resolved.Description;

                try
                {
                    MediaPlayer.Stop();
                }
                catch
                {
                    // ignore stop failures prior to retry
                }

                DisposeCurrentMedia();

                if (!string.IsNullOrWhiteSpace(module))
                {
                    MediaPlayer.SetOutputDevice(module, string.IsNullOrWhiteSpace(resolvedDeviceId) ? string.Empty : resolvedDeviceId);
                }

                Log.Information("[AUDIO] Trying backend {Module} with device {DeviceId}, HW={HW}", module, string.IsNullOrWhiteSpace(resolvedDeviceId) ? "<default>" : resolvedDeviceId, useHardwareDecoding);
                LogAudioAttempt(module, resolvedDeviceId, description, useHardwareDecoding);

                var pluginDirectory = _libVlcPluginDirectory;
                if (string.IsNullOrWhiteSpace(pluginDirectory) || !Directory.Exists(pluginDirectory))
                {
                    pluginDirectory = ResolveLibVlcPluginDirectory();
                }
                if (string.IsNullOrWhiteSpace(pluginDirectory) || !Directory.Exists(pluginDirectory))
                {
                    Log.Debug("[AUDIO] Using default LibVLC plugin search path for media options. Checked path: {PluginDir}",
                        pluginDirectory ?? "<none>");
                    pluginDirectory = null;
                }
                var mediaOptions = BuildMediaOptions(_settingsService.Settings.KaraokeVideoDevice, pluginDirectory, useHardwareDecoding);
                _currentMedia = new Media(_libVLC, new Uri(mediaPath), mediaOptions);

                if (!MediaPlayer.Play(_currentMedia))
                {
                    Log.Warning("[AUDIO] LibVLC failed to start playback (Module={Module}, DeviceId={DeviceId}, HW={HW})", module, string.IsNullOrWhiteSpace(resolvedDeviceId) ? "<default>" : resolvedDeviceId, useHardwareDecoding);
                    return false;
                }

                _lastPlaybackUsedHardwareDecoding = useHardwareDecoding;
                _hasTriedSoftwareFallbackForCurrentMedia = !useHardwareDecoding;

                if (!string.IsNullOrWhiteSpace(resolvedDeviceId))
                {
                    PersistAudioSelection(module, resolvedDeviceId);
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Warning("[AUDIO] Backend play failed ({Module}): {Message}", module, ex.Message);
                return false;
            }
        }

        private bool TryStartPlaybackWithRetries(string mediaPath, bool isDiagnostic, bool preferHardware = true)
        {
            var selection = ApplyAudioOutputSelection();
            var primaryModule = selection?.Module ?? _settingsService.Settings.AudioOutputModule ?? "mmdevice";
            var desiredDeviceId = selection?.DeviceId ?? _settingsService.Settings.AudioOutputDeviceId;

            // For diagnostics (test tone), prefer known-good defaults to avoid stale/invalid device IDs
            // that can silently fail. We'll try mmdevice with default endpoint first.
            if (isDiagnostic)
            {
                primaryModule = "mmdevice";
                desiredDeviceId = null; // default endpoint
            }

            var attemptedModules = new List<string>();
            if (!string.IsNullOrWhiteSpace(primaryModule))
            {
                attemptedModules.Add(primaryModule);
            }
            else
            {
                attemptedModules.Add("mmdevice");
            }

            if (_settingsService.Settings.AllowDirectSoundFallback && !attemptedModules.Any(m => string.Equals(m, "directsound", StringComparison.OrdinalIgnoreCase)))
            {
                attemptedModules.Add("directsound");
            }

            foreach (var backend in attemptedModules)
            {
                if (preferHardware && TryPlayWithBackend(backend, desiredDeviceId, mediaPath, useHardwareDecoding: true))
                {
                    return true;
                }

                if (TryPlayWithBackend(backend, desiredDeviceId, mediaPath, useHardwareDecoding: false))
                {
                    return true;
                }

                if (!preferHardware && TryPlayWithBackend(backend, desiredDeviceId, mediaPath, useHardwareDecoding: true))
                {
                    return true;
                }
            }

            Log.Error("[AUDIO] Playback attempts exhausted for {Path}. Checked backends: {Backends}", mediaPath, string.Join(", ", attemptedModules));
            if (!isDiagnostic)
            {
                Log.Error("[AUDIO] If Windows Volume Mixer shows this app muted or routed elsewhere, unmute/select the X32. Also check for other apps using exclusive access.");
            }

            return false;
        }

        private string CreateTestToneFile(double durationSeconds = 2.0)
        {
            const int sampleRate = 48000;
            const int channels = 2;
            const int bitsPerSample = 16;
            int totalSamples = (int)(sampleRate * durationSeconds);
            short amplitude = (short)(short.MaxValue * 0.25);
            int dataChunkSize = totalSamples * channels * (bitsPerSample / 8);
            int byteRate = sampleRate * channels * (bitsPerSample / 8);
            short blockAlign = (short)(channels * (bitsPerSample / 8));
            int chunkSize = 36 + dataChunkSize;

            string path = Path.Combine(Path.GetTempPath(), "BNKaraoke_TestTone.wav");
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            using var writer = new BinaryWriter(fs, Encoding.ASCII, leaveOpen: false);

            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(chunkSize);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)channels);
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write(blockAlign);
            writer.Write((short)bitsPerSample);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataChunkSize);

            for (int i = 0; i < totalSamples; i++)
            {
                double t = i / (double)sampleRate;
                short sample = (short)(Math.Sin(2 * Math.PI * 1000 * t) * amplitude);
                for (int channel = 0; channel < channels; channel++)
                {
                    writer.Write(sample);
                }
            }

            writer.Flush();
            return path;
        }

        private void DisposeCurrentMedia()
        {
            if (_currentMedia != null)
            {
                try
                {
                    _currentMedia.Dispose();
                    Log.Information("[VIDEO PLAYER] Disposed active media instance");
                }
                catch (Exception ex)
                {
                    Log.Warning("[VIDEO PLAYER] Failed to dispose media cleanly: {Message}", ex.Message);
                }
                finally
                {
                    _currentMedia = null;
                }
            }
        }
    }
}
