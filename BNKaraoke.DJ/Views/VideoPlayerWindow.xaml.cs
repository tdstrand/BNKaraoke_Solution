using BNKaraoke.DJ.Services;
using LibVLCSharp.Shared;
using Serilog;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace BNKaraoke.DJ.Views
{
    public partial class VideoPlayerWindow : Window
    {
        private readonly SettingsService _settingsService = SettingsService.Instance;
        private readonly LibVLC? _libVLC;
        public LibVLCSharp.Shared.MediaPlayer? MediaPlayer { get; private set; }
        private string? _currentVideoPath;
        private long _currentPosition;
        private DispatcherTimer? _hideVideoViewTimer;

        public event EventHandler? SongEnded;
        public new event EventHandler? Closed;
        public event EventHandler<MediaPlayerPositionChangedEventArgs>? PositionChanged;

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
                string toolsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TOOLS");
                if (!Directory.Exists(Path.Combine(toolsDir, "plugins")))
                {
                    Log.Warning("[VIDEO PLAYER] LibVLC plugins folder not found in {ToolsDir}. This may prevent video playback.", toolsDir);
                }

                try
                {
                    _libVLC = new LibVLC(
                        $"--plugin-path={toolsDir}",
                        "--no-video-title-show",
                        "--no-osd",
                        "--no-video-deco",
                        "--avcodec-hw=any" // Attempt hardware decoding
                    );
                    Log.Information("[VIDEO PLAYER] LibVLC initialized with hardware decoding, Version: {Version}, PluginPath: {ToolsDir}", _libVLC.Version, toolsDir);
                }
                catch (Exception ex)
                {
                    Log.Warning("[VIDEO PLAYER] Failed to initialize LibVLC with hardware decoding: {Message}. Falling back to software decoding.", ex.Message);
                    _libVLC = new LibVLC(
                        $"--plugin-path={toolsDir}",
                        "--no-video-title-show",
                        "--no-osd",
                        "--no-video-deco",
                        "--avcodec-hw=none"
                    );
                    Log.Information("[VIDEO PLAYER] LibVLC initialized with software decoding, Version: {Version}, PluginPath: {ToolsDir}", _libVLC.Version, toolsDir);
                }

                MediaPlayer = new LibVLCSharp.Shared.MediaPlayer(_libVLC);
                ShowInTaskbar = true;
                Owner = null;
                WindowStartupLocation = WindowStartupLocation.Manual;
                InitializeComponent();
                VideoPlayer.MediaPlayer = MediaPlayer;
                SourceInitialized += VideoPlayerWindow_SourceInitialized;
                Loaded += VideoPlayerWindow_Loaded;
                SizeChanged += VideoPlayerWindow_SizeChanged;
                MediaPlayer.EndReached += MediaPlayer_EndReached;
                MediaPlayer.PositionChanged += MediaPlayer_PositionChanged;
                MediaPlayer.EncounteredError += MediaPlayer_EncounteredError;
                _settingsService.AudioDeviceChanged += SettingsService_AudioDeviceChanged;
                Log.Information("[VIDEO PLAYER] Video player window initialized successfully");
            }
            catch (Exception ex)
            {
                Log.Error("[VIDEO PLAYER] Failed to initialize video player window: {Message}. Ensure libvlc.dll, libvlccore.dll, and plugins folder are in {ToolsDir}.", ex.Message, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TOOLS"));
                MessageBox.Show($"Failed to initialize video player: {ex.Message}. Ensure libvlc.dll, libvlccore.dll, and plugins folder are in the TOOLS directory.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        private void MediaPlayer_PositionChanged(object? sender, MediaPlayerPositionChangedEventArgs e)
        {
            PositionChanged?.Invoke(this, e);
        }

        private void MediaPlayer_EncounteredError(object? sender, EventArgs e)
        {
            Log.Error("[VIDEO PLAYER] VLC encountered an error during playback");
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                StopVideo();
                MessageBox.Show("Playback error occurred in VLC.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }

        private void VideoPlayerWindow_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            VideoPlayer.Width = e.NewSize.Width;
            VideoPlayer.Height = e.NewSize.Height;
        }

        private void MediaPlayer_EndReached(object? sender, EventArgs e)
        {
            StopVideo();
            SongEnded?.Invoke(this, EventArgs.Empty);
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
                    string toolsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TOOLS");
                    using var media = new Media(_libVLC, new Uri(_currentVideoPath),
                        $"--directx-device={_settingsService.Settings.KaraokeVideoDevice}",
                        $"--audio-device={deviceId}",
                        $"--plugin-path={toolsDir}",
                        "--no-video-title-show",
                        "--no-osd",
                        "--no-video-deco",
                        "--avcodec-hw=any");
                    MediaPlayer.Play(media);
                    MediaPlayer.Time = _currentPosition;
                    if (!wasPlaying)
                    {
                        MediaPlayer.Pause();
                    }
                    Application.Current.Dispatcher.InvokeAsync(() =>
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
                if (!File.Exists(videoPath))
                {
                    throw new FileNotFoundException($"Video file not found: {videoPath}");
                }

                string device = _settingsService.Settings.KaraokeVideoDevice;
                string toolsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TOOLS");
                _currentVideoPath = videoPath;
                if (_hideVideoViewTimer != null)
                {
                    _hideVideoViewTimer.Stop();
                    Log.Information("[VIDEO PLAYER] Stopped hide timer for playback");
                }
                TitleOverlay.Visibility = Visibility.Collapsed;
                VideoPlayer.Visibility = Visibility.Visible;
                VideoPlayer.Width = ActualWidth;
                VideoPlayer.Height = ActualHeight;
                VideoPlayer.UpdateLayout();

                Task.Run(() =>
                {
                    try
                    {
                        if (MediaPlayer.IsPlaying || MediaPlayer.State == VLCState.Paused)
                        {
                            MediaPlayer.Play();
                            Log.Information("[VIDEO PLAYER] Resuming video from paused state");
                        }
                        else
                        {
                            using var media = new Media(_libVLC, new Uri(videoPath),
                                $"--directx-device={device}",
                                $"--plugin-path={toolsDir}",
                                "--no-video-title-show",
                                "--no-osd",
                                "--no-video-deco",
                                "--avcodec-hw=any");
                            MediaPlayer.Play(media);
                            Log.Information("[VIDEO PLAYER] Starting new video with hardware decoding");
                        }

                        Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            try
                            {
                                WindowStyle = WindowStyle.None;
                                WindowState = WindowState.Maximized;
                                SetDisplayDevice();
                                var hwnd = new WindowInteropHelper(this).Handle;
                                var currentScreen = System.Windows.Forms.Screen.FromHandle(hwnd);
                                Log.Information("[VIDEO PLAYER] VLC state: IsPlaying={IsPlaying}, State={State}, Fullscreen={Fullscreen}",
                                    MediaPlayer?.IsPlaying ?? false, MediaPlayer?.State ?? VLCState.NothingSpecial, MediaPlayer?.Fullscreen ?? false);
                                Log.Information("[VIDEO PLAYER] Window bounds after play: Left={Left}, Top={Top}, Width={Width}, Height={Height}, Screen={Screen}",
                                    Left, Top, Width, Height, currentScreen.DeviceName);
                                Log.Information("[VIDEO PLAYER] WindowStyle={WindowStyle}, ShowInTaskbar={ShowInTaskbar}", WindowStyle, ShowInTaskbar);
                            }
                            catch (Exception ex)
                            {
                                Log.Error("[VIDEO PLAYER] Failed to update UI after play: {Message}", ex.Message);
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        Log.Error("[VIDEO PLAYER] Playback error with hardware decoding: {Message}. Attempting software decoding.", ex.Message);
                        try
                        {
                            if (MediaPlayer != null)
                            {
                                MediaPlayer.Stop();
                                using var media = new Media(_libVLC, new Uri(videoPath),
                                    $"--directx-device={device}",
                                    $"--plugin-path={toolsDir}",
                                    "--no-video-title-show",
                                    "--no-osd",
                                    "--no-video-deco",
                                    "--avcodec-hw=none");
                                MediaPlayer.Play(media);
                                Log.Information("[VIDEO PLAYER] Starting new video with software decoding");
                            }
                        }
                        catch (Exception ex2)
                        {
                            Log.Error("[VIDEO PLAYER] Playback error with software decoding: {Message}", ex2.Message);
                            Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                MessageBox.Show($"Failed to play video: {ex2.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            });
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error("[VIDEO PLAYER] Failed to play video: {VideoPath}, Message: {Message}", videoPath, ex.Message);
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Failed to play video: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
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
                    TitleOverlay.Visibility = Visibility.Visible;
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
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Failed to pause video: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
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
                    TitleOverlay.Visibility = Visibility.Visible;
                    Log.Information("[VIDEO PLAYER] Video stopped, VLC state: IsPlaying={IsPlaying}, State={State}",
                        MediaPlayer.IsPlaying, MediaPlayer.State);
                }
                else
                {
                    TitleOverlay.Visibility = Visibility.Visible;
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

        public void RestartVideo()
        {
            try
            {
                Log.Information("[VIDEO PLAYER] Restarting video");
                if (MediaPlayer != null)
                {
                    MediaPlayer.Time = 0;
                    VideoPlayer.Visibility = Visibility.Visible;
                    Visibility = Visibility.Visible;
                    Show();
                    Activate();
                    MediaPlayer.Play();
                    Log.Information("[VIDEO PLAYER] Video restarted, VLC state: IsPlaying={IsPlaying}, State={State}",
                        MediaPlayer.IsPlaying, MediaPlayer.State);
                }
                else
                {
                    Log.Information("[VIDEO PLAYER] MediaPlayer is null, cannot restart video");
                }
            }
            catch (Exception ex)
            {
                Log.Error("[VIDEO PLAYER] Failed to restart video: {Message}", ex.Message);
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
                    bool result = SetWindowPos(hwnd, IntPtr.Zero, bounds.Left, bounds.Top, bounds.Width, bounds.Height, (uint)(SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE | SetWindowPosFlags.SWP_NOREDRAW));
                    WindowStyle = WindowStyle.None;
                    WindowState = WindowState.Maximized;
                    Visibility = Visibility.Visible;
                    Show();
                    Activate();
                    Log.Information("[VIDEO PLAYER] SetWindowPos to {Device}, Position: {Left}x{Top}, Size: {Width}x{Height}, Success: {Result}, Flags: {Flags}",
                        targetDevice, bounds.Left, bounds.Top, bounds.Width, bounds.Height, result, (uint)(SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE | SetWindowPosFlags.SWP_NOREDRAW));
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
                        bool result = SetWindowPos(hwnd, IntPtr.Zero, bounds.Left, bounds.Top, bounds.Width, bounds.Height, (uint)(SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE | SetWindowPosFlags.SWP_NOREDRAW));
                        WindowStyle = WindowStyle.None;
                        WindowState = WindowState.Maximized;
                        Visibility = Visibility.Visible;
                        Show();
                        Activate();
                        Log.Information("[VIDEO PLAYER] Fallback to primary, Position: {Left}x{Top}, Size: {Width}x{Height}, Success: {Result}, Flags: {Flags}",
                            bounds.Left, bounds.Top, bounds.Width, bounds.Height, result, (uint)(SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE | SetWindowPosFlags.SWP_NOREDRAW));
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
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Failed to set display device: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
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
                _hideVideoViewTimer?.Stop();
                if (MediaPlayer != null)
                {
                    if (MediaPlayer.IsPlaying || MediaPlayer.State == VLCState.Paused)
                    {
                        MediaPlayer.Stop();
                    }
                    MediaPlayer.PositionChanged -= MediaPlayer_PositionChanged;
                    MediaPlayer.EndReached -= MediaPlayer_EndReached;
                    MediaPlayer.EncounteredError -= MediaPlayer_EncounteredError;
                    MediaPlayer.Dispose();
                    MediaPlayer = null;
                }
                if (_libVLC != null)
                {
                    _libVLC.Dispose();
                }
                _settingsService.AudioDeviceChanged -= SettingsService_AudioDeviceChanged;
                Closed?.Invoke(this, EventArgs.Empty);
                Log.Information("[VIDEO PLAYER] Video player window closed successfully");
            }
            catch (Exception ex)
            {
                Log.Error("[VIDEO PLAYER] Error during cleanup: {Message}", ex.Message);
            }
            base.OnClosed(e);
        }
    }
}