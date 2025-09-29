using BNKaraoke.DJ.Models;
using BNKaraoke.DJ.Services;
using LibVLCSharp.Shared;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
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
        private LibVLC? _libVLC;
        public LibVLCSharp.Shared.MediaPlayer? MediaPlayer { get; private set; }
        private Media? _currentMedia;
        private string? _currentVideoPath;
        private long _currentPosition;
        private DispatcherTimer? _hideVideoViewTimer;
        private Equalizer? _equalizer;
        private readonly object _audioSelectionLock = new();
        private bool _suppressSongEnded;

        public event EventHandler? SongEnded;
        public new event EventHandler? Closed;
        public event EventHandler<MediaPlayerPositionChangedEventArgs>? PositionChanged;
        public event EventHandler? MediaPlayerReinitialized;
        public event EventHandler<long>? MediaLengthChanged;

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

        private string[] BuildMediaOptions(string videoDevice, string toolsDir, bool useHardwareDecoding)
        {
            var options = new List<string>
            {
                $"--directx-device={videoDevice}",
                $"--plugin-path={toolsDir}",
                "--no-video-title-show",
                "--no-osd",
                "--no-video-deco",
                $"--avcodec-hw={(useHardwareDecoding ? "any" : "none")}" 
            };

            return options.ToArray();
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

                ShowInTaskbar = true;
                Owner = null;
                WindowStartupLocation = WindowStartupLocation.Manual;
                InitializeComponent();
                InitializeMediaPlayer();
                SourceInitialized += VideoPlayerWindow_SourceInitialized;
                Loaded += VideoPlayerWindow_Loaded;
                SizeChanged += VideoPlayerWindow_SizeChanged;
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
                    MediaPlayer.Dispose();
                    Log.Information("[VIDEO PLAYER] Disposed previous MediaPlayer instance before reinitialization");
                }

                MediaPlayer = new LibVLCSharp.Shared.MediaPlayer(_libVLC);
                MediaPlayer.EndReached += MediaPlayer_EndReached;
                MediaPlayer.PositionChanged += MediaPlayer_PositionChanged;
                MediaPlayer.EncounteredError += MediaPlayer_EncounteredError;
                MediaPlayer.LengthChanged += MediaPlayer_LengthChanged;
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
                    var module = _settingsService.Settings.AudioOutputModule ?? "mmdevice";
                    using var outputs = _libVLC.AudioOutputList();
                    var moduleInfo = outputs?.FirstOrDefault(o => string.Equals(o.Name, module, StringComparison.OrdinalIgnoreCase));

                    if (moduleInfo == null)
                    {
                        Log.Warning("[AUDIO] Output module {Module} not found, defaulting to mmdevice", module);
                        module = "mmdevice";
                        moduleInfo = outputs?.FirstOrDefault(o => string.Equals(o.Name, "mmdevice", StringComparison.OrdinalIgnoreCase));
                    }

                    if (moduleInfo == null)
                    {
                        Log.Warning("[AUDIO] No audio output modules discovered");
                        return null;
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
                StopVideo();
                MessageBox.Show("Playback error occurred in VLC. If Windows Volume Mixer shows this app muted or routed to another endpoint, unmute/select the X32. Also check if another application is using the device in exclusive mode.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }

        private void VideoPlayerWindow_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            VideoPlayer.Width = e.NewSize.Width;
            VideoPlayer.Height = e.NewSize.Height;
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
                            WindowStyle = WindowStyle.None;
                            WindowState = WindowState.Maximized;
                            SetDisplayDevice();
                        });
                        Log.Information("[VIDEO PLAYER] Switched audio device, resumed at position: {Position}ms", _currentPosition);
                    }
                    else
                    {
                        Log.Error("[VIDEO PLAYER] Failed to resume after audio device change");
                        MessageBox.Show("Failed to switch audio device. Check for exclusive access or muted output and try again.", "Audio", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
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

                if (!File.Exists(videoPath))
                {
                    throw new FileNotFoundException($"Video file not found: {videoPath}");
                }

                string? previousPath = _currentVideoPath;
                _suppressSongEnded = isDiagnostic;

                if (!isDiagnostic)
                {
                    _currentVideoPath = videoPath;
                }

                if (!isDiagnostic && _hideVideoViewTimer != null)
                {
                    _hideVideoViewTimer.Stop();
                    Log.Information("[VIDEO PLAYER] Stopped hide timer for playback");
                }

                if (!isDiagnostic)
                {
                    TitleOverlay.Visibility = Visibility.Collapsed;
                    VideoPlayer.Visibility = Visibility.Visible;
                    VideoPlayer.Width = ActualWidth;
                    VideoPlayer.Height = ActualHeight;
                    VideoPlayer.UpdateLayout();
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
                        MessageBox.Show($"Failed to play test tone: {ex.Message}\nVerify the selected audio device and exclusive-mode settings.", "Audio", MessageBoxButton.OK, MessageBoxImage.Warning);
                    });
                }
                else
                {
                    Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show($"Failed to play video: {ex.Message}\nIf Windows Volume Mixer shows this app muted or routed to another endpoint, unmute/select the X32. Also check if any other app is holding exclusive access.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
            }
            finally
            {
                if (isDiagnostic && !playbackStarted)
                {
                    _suppressSongEnded = previousSuppress;
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
                    MediaPlayer.Dispose();
                    MediaPlayer = null;
                }

                _libVLC?.Dispose();

                string toolsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TOOLS");
                try
                {
                    _libVLC = new LibVLC($"--plugin-path={toolsDir}", "--no-video-title-show", "--no-osd", "--no-video-deco", "--avcodec-hw=any");
                }
                catch (Exception ex)
                {
                    Log.Warning("[AUDIO] Restart fallback to software decoding: {Message}", ex.Message);
                    _libVLC = new LibVLC($"--plugin-path={toolsDir}", "--no-video-title-show", "--no-osd", "--no-video-deco", "--avcodec-hw=none");
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
                            WindowStyle = WindowStyle.None;
                            WindowState = WindowState.Maximized;
                            SetDisplayDevice();
                        });
                    }
                    else
                    {
                        Log.Error("[AUDIO] Failed to resume playback after audio engine restart for {Path}", path);
                        MessageBox.Show("Failed to restart playback after rebuilding the audio engine. Check the audio device and try again.", "Audio", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }

                Log.Information("[AUDIO] Audio engine restarted");
            }
            catch (Exception ex)
            {
                Log.Error("[AUDIO] Restart engine failed: {Message}", ex.Message);
                MessageBox.Show($"Failed to restart audio engine: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show($"Failed to play test tone: {ex.Message}", "Audio", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    DisposeCurrentMedia();
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
                _suppressSongEnded = false;
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

                VideoPlayer.Visibility = Visibility.Visible;
                Visibility = Visibility.Visible;
                Show();
                Activate();

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
                    WindowStyle = WindowStyle.None;
                    WindowState = WindowState.Maximized;
                    SetDisplayDevice();
                });

                Log.Information("[VIDEO PLAYER] Video restarted with new LibVLC media session, VLC state: IsPlaying={IsPlaying}, State={State}",
                    MediaPlayer.IsPlaying, MediaPlayer.State);
            }
            catch (Exception ex)
            {
                Log.Error("[VIDEO PLAYER] Failed to restart video: {Message}", ex.Message);
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Failed to restart video: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    DisposeCurrentMedia();
                    MediaPlayer.PositionChanged -= MediaPlayer_PositionChanged;
                    MediaPlayer.EndReached -= MediaPlayer_EndReached;
                    MediaPlayer.EncounteredError -= MediaPlayer_EncounteredError;
                    MediaPlayer.LengthChanged -= MediaPlayer_LengthChanged;
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

                var toolsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TOOLS");
                var mediaOptions = BuildMediaOptions(_settingsService.Settings.KaraokeVideoDevice, toolsDir, useHardwareDecoding);
                _currentMedia = new Media(_libVLC, new Uri(mediaPath), mediaOptions);

                if (!MediaPlayer.Play(_currentMedia))
                {
                    Log.Warning("[AUDIO] LibVLC failed to start playback (Module={Module}, DeviceId={DeviceId}, HW={HW})", module, string.IsNullOrWhiteSpace(resolvedDeviceId) ? "<default>" : resolvedDeviceId, useHardwareDecoding);
                    return false;
                }

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

        private bool TryStartPlaybackWithRetries(string mediaPath, bool isDiagnostic)
        {
            var selection = ApplyAudioOutputSelection();
            var primaryModule = selection?.Module ?? _settingsService.Settings.AudioOutputModule ?? "mmdevice";
            var desiredDeviceId = selection?.DeviceId ?? _settingsService.Settings.AudioOutputDeviceId;

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
                if (TryPlayWithBackend(backend, desiredDeviceId, mediaPath, useHardwareDecoding: true))
                {
                    return true;
                }

                if (TryPlayWithBackend(backend, desiredDeviceId, mediaPath, useHardwareDecoding: false))
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