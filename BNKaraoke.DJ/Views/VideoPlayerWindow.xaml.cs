using BNKaraoke.DJ.Services;
using LibVLCSharp.Shared;
using Serilog;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace BNKaraoke.DJ.Views;

public partial class VideoPlayerWindow : Window
{
    private readonly SettingsService _settingsService = SettingsService.Instance;
    private readonly LibVLC? _libVLC;
    public LibVLCSharp.Shared.MediaPlayer? MediaPlayer { get; private set; }
    private bool _isDisposing;
    private string? _currentVideoPath;
    private long _currentPosition;
    private DispatcherTimer? _hideVideoViewTimer;
    private bool _isShowEnding;

    public event EventHandler? SongEnded;
    public event EventHandler<MediaPlayerTimeChangedEventArgs>? TimeChanged;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

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

    public VideoPlayerWindow()
    {
        try
        {
            Log.Information("[VIDEO PLAYER] Initializing video player window");
            _libVLC = new LibVLC("--no-video-title-show", "--no-osd", "--no-video-deco");
            MediaPlayer = new LibVLCSharp.Shared.MediaPlayer(_libVLC);
            ShowInTaskbar = true;
            Owner = null;
            WindowStartupLocation = WindowStartupLocation.Manual;
            InitializeComponent();
            VideoPlayer.MediaPlayer = MediaPlayer;
            SourceInitialized += VideoPlayerWindow_SourceInitialized;
            Loaded += VideoPlayerWindow_Loaded;
            _settingsService.AudioDeviceChanged += SettingsService_AudioDeviceChanged;
            Log.Information("[VIDEO PLAYER] Video player window initialized successfully");
        }
        catch (Exception ex)
        {
            Log.Error("[VIDEO PLAYER] Failed to initialize video player window: {Message}", ex.Message);
            MessageBox.Show($"Failed to initialize video player: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    private void SettingsService_AudioDeviceChanged(object? sender, string deviceId)
    {
        try
        {
            Log.Information("[VIDEO PLAYER] Audio device changed: {DeviceId}", deviceId);
            if (MediaPlayer != null && !string.IsNullOrEmpty(_currentVideoPath) && _libVLC != null)
            {
                _currentPosition = MediaPlayer.Time;
                bool wasPlaying = MediaPlayer.IsPlaying;
                MediaPlayer.Stop();
                VideoPlayer.Visibility = Visibility.Visible;
                using var media = new Media(_libVLC, new Uri(_currentVideoPath), $"--directx-device={_settingsService.Settings.KaraokeVideoDevice}", $"--audio-device={deviceId}", "--no-video-title-show", "--no-osd", "--no-video-deco");
                MediaPlayer.Play(media);
                MediaPlayer.Time = _currentPosition;
                if (!wasPlaying)
                {
                    MediaPlayer.Pause();
                }
                Application.Current.Dispatcher.Invoke(() =>
                {
                    WindowStyle = WindowStyle.None;
                    WindowState = WindowState.Maximized;
                    SetDisplayDevice();
                });
                Log.Information("[VIDEO PLAYER] Switched audio device, resumed at position: {Position}ms", _currentPosition);
            }
        }
        catch (Exception ex)
        {
            Log.Error("[VIDEO PLAYER] Failed to switch audio device: {Message}", ex.Message);
            MessageBox.Show($"Failed to switch audio device: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void VideoPlayerWindow_SourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            Log.Information("[VIDEO PLAYER] Source initialized, setting display");
            SetDisplayDevice();
            Show();
            Activate();
            Log.Information("[VIDEO PLAYER] Window visibility after SourceInitialized: {Visibility}, ShowInTaskbar: {ShowInTaskbar}", Visibility, ShowInTaskbar);
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
            WindowState = WindowState.Maximized;
            ShowActivated = true;

            if (MediaPlayer != null)
            {
                MediaPlayer.EndReached += MediaPlayerEnded;
                MediaPlayer.TimeChanged += MediaPlayer_TimeChanged;
                Log.Information("[VIDEO PLAYER] MediaPlayer initialized, IsPlaying={IsPlaying}, State={State}", MediaPlayer.IsPlaying, MediaPlayer.State);
            }

            Visibility = Visibility.Visible;
            VideoPlayer.Visibility = Visibility.Visible;
            Show();
            Activate();

            _hideVideoViewTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3),
                IsEnabled = true
            };
            _hideVideoViewTimer.Tick += (s, args) =>
            {
                VideoPlayer.Visibility = Visibility.Collapsed;
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
            Log.Information("[VIDEO PLAYER] VideoView bounds: Width={Width}, Height={Height}, ActualWidth={ActualWidth}, ActualHeight={ActualHeight}",
                VideoPlayer.Width, VideoPlayer.Height, VideoPlayer.ActualWidth, VideoPlayer.ActualHeight);
            Log.Information("[VIDEO PLAYER] Window visibility: {Visibility}, ShowInTaskbar: {ShowInTaskbar}", Visibility, ShowInTaskbar);
        }
        catch (Exception ex)
        {
            Log.Error("[VIDEO PLAYER] Failed to set display on load: {Message}", ex.Message);
            MessageBox.Show($"Failed to set display: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void PlayVideo(string videoPath)
    {
        try
        {
            Log.Information("[VIDEO PLAYER] Attempting to play video: {VideoPath}", videoPath);
            if (_libVLC == null || MediaPlayer == null) throw new InvalidOperationException("Media player not initialized");
            string device = _settingsService.Settings.KaraokeVideoDevice;
            _currentVideoPath = videoPath;
            if (_hideVideoViewTimer != null)
            {
                _hideVideoViewTimer.Stop();
                Log.Information("[VIDEO PLAYER] Stopped hide timer for playback");
            }
            VideoPlayer.Visibility = Visibility.Visible;
            if (MediaPlayer.IsPlaying || MediaPlayer.State == VLCState.Paused)
            {
                MediaPlayer.Play();
                Log.Information("[VIDEO PLAYER] Resuming video from paused state");
            }
            else
            {
                using var media = new Media(_libVLC, new Uri(videoPath), $"--directx-device={device}", "--no-video-title-show", "--no-osd", "--no-video-deco");
                MediaPlayer.Play(media);
                Log.Information("[VIDEO PLAYER] Starting new video");
            }
            Visibility = Visibility.Visible;
            Show();
            Activate();
            Application.Current.Dispatcher.Invoke(() =>
            {
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
                SetDisplayDevice();
                var hwnd = new WindowInteropHelper(this).Handle;
                var currentScreen = System.Windows.Forms.Screen.FromHandle(hwnd);
                Log.Information("[VIDEO PLAYER] VLC state: IsPlaying={IsPlaying}, State={State}, Fullscreen={Fullscreen}",
                    MediaPlayer.IsPlaying, MediaPlayer.State, MediaPlayer.Fullscreen);
                Log.Information("[VIDEO PLAYER] Window bounds after play: Left={Left}, Top={Top}, Width={Width}, Height={Height}, Screen={Screen}",
                    Left, Top, Width, Height, currentScreen.DeviceName);
                Log.Information("[VIDEO PLAYER] WindowStyle={WindowStyle}, ShowInTaskbar={ShowInTaskbar}", WindowStyle, ShowInTaskbar);
            });
        }
        catch (Exception ex)
        {
            Log.Error("[VIDEO PLAYER] Failed to play video: {VideoPath}, Message: {Message}", videoPath, ex.Message);
            MessageBox.Show($"Failed to play video: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                Visibility = Visibility.Visible;
                Show();
                Activate();
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
            MessageBox.Show($"Failed to pause video: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void StopVideo()
    {
        try
        {
            Log.Information("[VIDEO PLAYER] Stopping video");
            if (MediaPlayer != null && (MediaPlayer.IsPlaying || MediaPlayer.State == VLCState.Paused))
            {
                MediaPlayer.Stop();
                VideoPlayer.Visibility = Visibility.Collapsed;
                Visibility = Visibility.Visible;
                Show();
                Activate();
                SetDisplayDevice();
                Log.Information("[VIDEO PLAYER] Video stopped");
            }
            else
            {
                Log.Information("[VIDEO PLAYER] No video playing or paused to stop");
            }
            _currentVideoPath = null;
            _currentPosition = 0;
        }
        catch (Exception ex)
        {
            Log.Error("[VIDEO PLAYER] Failed to stop video: {Message}", ex.Message);
        }
    }

    public void EndShow()
    {
        _isShowEnding = true;
        Close();
    }

    internal void SetDisplayDevice()
    {
        try
        {
            string targetDevice = _settingsService.Settings.KaraokeVideoDevice;
            Log.Information("[VIDEO PLAYER] Setting display to: {Device}", targetDevice);

            var targetScreen = System.Windows.Forms.Screen.AllScreens.FirstOrDefault(
                screen => screen.DeviceName.Equals(targetDevice, StringComparison.OrdinalIgnoreCase));

            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                Log.Information("[VIDEO PLAYER] Detected screen: {DeviceName}, Bounds: {Left}x{Top} {Width}x{Height}, Primary: {Primary}",
                    screen.DeviceName, screen.Bounds.Left, screen.Bounds.Top, screen.Bounds.Width, screen.Bounds.Height, screen.Primary);
            }

            if (targetScreen != null)
            {
                var bounds = targetScreen.Bounds;
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                bool result = SetWindowPos(hwnd, IntPtr.Zero, bounds.Left, bounds.Top, bounds.Width, bounds.Height, (uint)(SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE));
                Log.Information("[VIDEO PLAYER] SetWindowPos to {Device}, Position: {Left}x{Top}, Size: {Width}x{Height}, Success: {Result}, Flags: {Flags}",
                    targetDevice, bounds.Left, bounds.Top, bounds.Width, bounds.Height, result, (uint)(SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE));
                var currentScreen = System.Windows.Forms.Screen.FromHandle(hwnd);
                Log.Information("[VIDEO PLAYER] Current screen after SetWindowPos: {DeviceName}", currentScreen.DeviceName);
            }
            else
            {
                Log.Warning("[VIDEO PLAYER] Target device not found: {Device}, falling back to primary monitor", targetDevice);
                var primaryScreen = System.Windows.Forms.Screen.PrimaryScreen;
                if (primaryScreen != null)
                {
                    var bounds = primaryScreen.Bounds;
                    IntPtr hwnd = new WindowInteropHelper(this).Handle;
                    bool result = SetWindowPos(hwnd, IntPtr.Zero, bounds.Left, bounds.Top, bounds.Width, bounds.Height, (uint)(SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE));
                    Log.Information("[VIDEO PLAYER] Fallback to primary, Position: {Left}x{Top}, Size: {Width}x{Height}, Success: {Result}, Flags: {Flags}",
                        bounds.Left, bounds.Top, bounds.Width, bounds.Height, result, (uint)(SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE));
                }
                else
                {
                    Log.Error("[VIDEO PLAYER] No primary screen found");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("[VIDEO PLAYER] Failed to set display device: {Message}", ex.Message);
            MessageBox.Show($"Failed to set display device: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void MediaPlayerEnded(object? sender, EventArgs e)
    {
        if (_isDisposing) return;
        try
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Log.Information("[VIDEO PLAYER] Media ended");
                if (MediaPlayer != null)
                {
                    MediaPlayer.Stop();
                    VideoPlayer.Visibility = Visibility.Collapsed;
                }
                Visibility = Visibility.Visible;
                Show();
                Activate();
                SetDisplayDevice();
                Log.Information("[VIDEO PLAYER] Invoking SongEnded event");
                SongEnded?.Invoke(this, EventArgs.Empty);
                _currentVideoPath = null;
                _currentPosition = 0;
            });
        }
        catch (Exception ex)
        {
            Log.Error("[VIDEO PLAYER] Failed to process MediaEnded event: {Message}, StackTrace={StackTrace}", ex.Message, ex.StackTrace);
        }
    }

    private void MediaPlayer_TimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
    {
        if (_isDisposing) return;
        try
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                TimeChanged?.Invoke(this, e);
            });
        }
        catch (Exception ex)
        {
            Log.Error("[VIDEO PLAYER] Failed to process TimeChanged event: {Message}", ex.Message);
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_isShowEnding)
        {
            e.Cancel = true;
            Log.Information("[VIDEO PLAYER] Prevented window closing; show not ended");
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _isDisposing = true;
        try
        {
            Log.Information("[VIDEO PLAYER] Closing video player window");
            _hideVideoViewTimer?.Stop();
            if (MediaPlayer != null)
            {
                MediaPlayer.Stop();
                MediaPlayer.TimeChanged -= MediaPlayer_TimeChanged;
                MediaPlayer.EndReached -= MediaPlayerEnded;
                MediaPlayer.Dispose();
                MediaPlayer = null;
            }
            _libVLC?.Dispose();
            _settingsService.AudioDeviceChanged -= SettingsService_AudioDeviceChanged;
        }
        catch (Exception ex)
        {
            Log.Error("[VIDEO PLAYER] Error during cleanup: {Message}", ex.Message);
        }
        base.OnClosed(e);
    }
}