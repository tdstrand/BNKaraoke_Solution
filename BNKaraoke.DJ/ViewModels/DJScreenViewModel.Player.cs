using BNKaraoke.DJ.Models;
using BNKaraoke.DJ.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using Serilog;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Timer = System.Timers.Timer;

namespace BNKaraoke.DJ.ViewModels
{
    public partial class DJScreenViewModel
    {
        private Timer? _warningTimer;
        private Timer? _countdownTimer;
        private DispatcherTimer? _updateTimer;
        private Timer? _debounceTimer;
        private bool _isDisposing;
        private bool _isTearingDownShowVisuals;
        private TimeSpan? _totalDuration;
        private bool _countdownStarted;
        private bool _isSeeking;
        public bool IsSeeking => _isSeeking;
        private bool _isInitialPlayback;
        private bool _wasPlaying;
        private readonly object _queueLock = new();
        private readonly object _stateLock = new();
        private double _pendingSeekPosition;
        private double _lastPosition;
        private double _baseVolume = 100;
        private double? _fadeStartTimeSeconds;
        private double? _introMuteSeconds;
        private double? _configuredFadeStartSeconds;
        private bool _manualFadeActive;
        private LibVLCSharp.Shared.MediaPlayer? _attachedMediaPlayer;
        private TimeSpan? _fullSongDuration;

        partial void OnIsPlayingChanged(bool value) => RefreshAudioRecoveryCommands();
        partial void OnIsVideoPausedChanged(bool value) => RefreshAudioRecoveryCommands();
        partial void OnIsShowActiveChanged(bool value) => RefreshAudioRecoveryCommands();
        partial void OnCurrentShowStateChanged(ShowState value)
        {
            Log.Information("[DJSCREEN] Show state changed to {State}", value);
            RefreshAudioRecoveryCommands();
        }

        partial void OnBassBoostChanged(int value)
        {
            _videoPlayerWindow?.SetBassGain(value);
        }

        private void RefreshAudioRecoveryCommands()
        {
            (RestartAudioEngineCommand as IRelayCommand)?.NotifyCanExecuteChanged();
            (PlayTestToneCommand as IRelayCommand)?.NotifyCanExecuteChanged();
        }

        private bool InitializeShowVisuals(out string? errorMessage)
        {
            errorMessage = null;
            string? localErrorMessage = null;

            if (_videoPlayerWindow != null)
            {
                EnsureOverlayBindingsActive();
                Log.Information("[SHOW] Initialize visuals skipped: window already active");
                return true;
            }

            Log.Information("[SHOW] Initialize visuals (window+overlay+marquee)");
            try
            {
                void Initialize()
                {
                    var window = new VideoPlayerWindow();

                    if (window.MediaPlayer == null)
                    {
                        localErrorMessage = "Video player initialization failed. Check LibVLC setup.";
                        Log.Error("[DJSCREEN] VideoPlayerWindow initialization failed: MediaPlayer is null");
                        window.Close();
                        return;
                    }

                    window.SetBassGain(BassBoost);
                    window.SongEnded += VideoPlayerWindow_SongEnded;
                    window.Closed += VideoPlayerWindow_Closed;
                    window.MediaPlayerReinitialized += VideoPlayerWindow_MediaPlayerReinitialized;
                    window.MediaLengthChanged += VideoPlayerWindow_MediaLengthChanged;

                    _videoPlayerWindow = window;
                    RefreshAudioRecoveryCommands();
                    AttachMediaPlayerHandlers(window.MediaPlayer);
                    VideoPlayerWindow_MediaPlayerReinitialized(window, EventArgs.Empty);
                    window.ShowWindow();
                    window.ShowIdleScreen();
                    EnsureOverlayBindingsActive();
                    Log.Information("[DJSCREEN] Show visuals initialized and idle screen displayed");
                }

                if (Application.Current.Dispatcher.CheckAccess())
                {
                    Initialize();
                }
                else
                {
                    Application.Current.Dispatcher.Invoke(Initialize);
                }
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to initialize show visuals: {Message}", ex.Message);
                localErrorMessage ??= ex.Message;
                errorMessage = localErrorMessage;
                CleanupVideoPlayerWindow();
                return false;
            }

            errorMessage = localErrorMessage;

            if (_videoPlayerWindow == null)
            {
                CleanupVideoPlayerWindow();
                return false;
            }

            if (!string.IsNullOrWhiteSpace(localErrorMessage))
            {
                CleanupVideoPlayerWindow();
                return false;
            }

            return true;
        }

        private bool EnsureShowVisualsReady(out string? errorMessage)
        {
            errorMessage = null;

            if (_videoPlayerWindow != null)
            {
                return true;
            }

            if (CurrentShowState is ShowState.PreShow or ShowState.Ended)
            {
                errorMessage = "Show visuals are not available. Start the show to initialize the video player.";
                return false;
            }

            return InitializeShowVisuals(out errorMessage);
        }

        private void TeardownShowVisuals()
        {
            void PerformTeardown()
            {
                if (_isTearingDownShowVisuals)
                {
                    Log.Verbose("[DJSCREEN] TeardownShowVisuals skipped: already tearing down visuals");
                    return;
                }

                if (_videoPlayerWindow == null && !_overlayBindingsActive && _updateTimer == null && _countdownTimer == null && _debounceTimer == null && _warningTimer == null)
                {
                    Log.Verbose("[DJSCREEN] TeardownShowVisuals skipped: no active visuals to tear down");
                    return;
                }

                _isTearingDownShowVisuals = true;
                try
                {
                    Log.Information("[SHOW] Teardown visuals (close+dispose+unsub)");
                    var window = _videoPlayerWindow;
                    if (window != null)
                    {
                        CleanupVideoPlayerWindow();
                        try
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                try
                                {
                                    if (!window.Dispatcher.HasShutdownStarted && !window.Dispatcher.HasShutdownFinished && window.IsLoaded)
                                    {
                                        window.EndShow();
                                    }
                                    else
                                    {
                                        window.Close();
                                    }
                                }
                                catch (Exception closeEx)
                                {
                                    Log.Error("[DJSCREEN] Failed to close show visuals during teardown: {Message}", closeEx.Message);
                                }
                            });
                        }
                        catch (Exception ex)
                        {
                            Log.Error("[DJSCREEN] Failed to close show visuals: {Message}", ex.Message);
                        }
                    }

                    DeactivateOverlayBindings();

                    if (_updateTimer != null)
                    {
                        _updateTimer.Tick -= UpdateTimer_Tick;
                        _updateTimer.Stop();
                        _updateTimer = null;
                        Log.Information("[DJSCREEN] Stopped update timer during show teardown");
                    }

                    if (_countdownTimer != null)
                    {
                        _countdownTimer.Elapsed -= CountdownTimer_Elapsed;
                        _countdownTimer.Stop();
                        _countdownTimer.Dispose();
                        _countdownTimer = null;
                        Log.Information("[DJSCREEN] Stopped countdown timer during show teardown");
                    }

                    if (_debounceTimer != null)
                    {
                        _debounceTimer.Elapsed -= DebounceTimer_Elapsed;
                        _debounceTimer.Stop();
                        _debounceTimer.Dispose();
                        _debounceTimer = null;
                        Log.Information("[DJSCREEN] Stopped debounce timer during show teardown");
                    }

                    if (_warningTimer != null)
                    {
                        _warningTimer.Elapsed -= WarningTimer_Elapsed;
                        _warningTimer.Stop();
                        _warningTimer.Dispose();
                        _warningTimer = null;
                        Log.Information("[DJSCREEN] Stopped warning timer during show teardown");
                    }

                    ResetPlaybackState();
                }
                finally
                {
                    _isTearingDownShowVisuals = false;
                }
            }

            if (Application.Current.Dispatcher.CheckAccess())
            {
                PerformTeardown();
            }
            else
            {
                Application.Current.Dispatcher.Invoke(PerformTeardown);
            }
        }

        private void CleanupVideoPlayerWindow()
        {
            var window = _videoPlayerWindow;
            if (window == null)
            {
                return;
            }

            try
            {
                window.SongEnded -= VideoPlayerWindow_SongEnded;
                window.Closed -= VideoPlayerWindow_Closed;
                window.MediaPlayerReinitialized -= VideoPlayerWindow_MediaPlayerReinitialized;
                window.MediaLengthChanged -= VideoPlayerWindow_MediaLengthChanged;
            }
            catch (Exception ex)
            {
                Log.Debug("[DJSCREEN] Failed to detach video player window events: {Message}", ex.Message);
            }

            DetachMediaPlayerHandlers();
            _videoPlayerWindow = null;
            RefreshAudioRecoveryCommands();
            Log.Information("[DJSCREEN] Video player window references cleared");
        }

        public void SetWarningMessage(string message)
        {
            if (_isDisposing) return;
            try
            {
                WarningMessage = message;
                WarningExpirationTime = DateTime.Now.AddSeconds(30);
                if (_warningTimer == null)
                {
                    _warningTimer = new Timer(1000);
                    _warningTimer.Elapsed += WarningTimer_Elapsed;
                    _warningTimer.Start();
                }
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to set warning message: {Message}", ex.Message);
            }
        }

        private async Task SetWarningMessageAsync(string message)
        {
            await Application.Current.Dispatcher.InvokeAsync(() => SetWarningMessage(message));
        }

        private void WarningTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            if (_isDisposing) return;
            try
            {
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (WarningExpirationTime == null || DateTime.Now >= WarningExpirationTime)
                    {
                        WarningMessage = "";
                        WarningExpirationTime = null;
                        if (_warningTimer != null)
                        {
                            _warningTimer.Stop();
                            _warningTimer.Dispose();
                            _warningTimer = null;
                        }
                    }
                    OnPropertyChanged(nameof(WarningExpirationTime));
                });
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to process warning timer: {Message}", ex.Message);
            }
        }

        private void CountdownTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            if (_isDisposing) return;
            try
            {
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if ((IsPlaying || IsVideoPaused) && _totalDuration.HasValue && _videoPlayerWindow?.MediaPlayer != null)
                    {
                        try
                        {
                            var currentTime = TimeSpan.FromMilliseconds(_videoPlayerWindow.MediaPlayer.Time);
                            var remaining = _totalDuration.Value - currentTime;
                            var seconds = (int)Math.Max(0, remaining.TotalSeconds);
                            TimeRemainingSeconds = seconds;
                            TimeRemaining = TimeSpan.FromSeconds(seconds).ToString(@"m\:ss");
                            if (!_countdownStarted)
                            {
                                Log.Information("[DJSCREEN] Countdown started: {TimeRemaining}", TimeRemaining);
                                _countdownStarted = true;
                            }
                            CurrentVideoPosition = currentTime.ToString(@"m\:ss");
                            OnPropertyChanged(nameof(CurrentVideoPosition));
                            OnPropertyChanged(nameof(TimeRemaining));
                            OnPropertyChanged(nameof(TimeRemainingSeconds));
                            if (seconds == 0)
                            {
                                Log.Information("[DJSCREEN] Countdown ended");
                                _countdownStarted = false;
                                if (_videoPlayerWindow?.MediaPlayer != null && (IsPlaying || _videoPlayerWindow.MediaPlayer.IsPlaying))
                                {
                                    _videoPlayerWindow.StopVideo();
                                    _ = HandleSongEnded();
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error("[DJSCREEN] Countdown timer error: {Message}", ex.Message);
                        }
                    }
                    else
                    {
                        TimeRemainingSeconds = 0;
                        TimeRemaining = "--:--";
                        CurrentVideoPosition = "--:--";
                        SongPosition = 0;
                        _lastPosition = 0;
                        SongDuration = TimeSpan.Zero;
                        _totalDuration = null;
                        OnPropertyChanged(nameof(SongPosition));
                        OnPropertyChanged(nameof(CurrentVideoPosition));
                        OnPropertyChanged(nameof(TimeRemaining));
                        OnPropertyChanged(nameof(TimeRemainingSeconds));
                        OnPropertyChanged(nameof(SongDuration));
                        _countdownStarted = false;
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to process countdown timer: {Message}", ex.Message);
            }
        }

        private void UpdateTimer_Tick(object? sender, EventArgs e)
        {
            if (_isDisposing || _isSeeking || _isInitialPlayback || _videoPlayerWindow?.MediaPlayer == null || !IsPlaying || _videoPlayerWindow.MediaPlayer.State == VLCState.Stopped)
            {
                Log.Verbose("[DJSCREEN] UpdateTimer_Tick skipped: Disposing={Disposing}, Seeking={Seeking}, InitialPlayback={InitialPlayback}, MediaPlayer={MediaPlayer}, IsPlaying={IsPlaying}, State={State}",
                    _isDisposing, _isSeeking, _isInitialPlayback, _videoPlayerWindow?.MediaPlayer != null, IsPlaying, _videoPlayerWindow?.MediaPlayer?.State ?? VLCState.NothingSpecial);
                return;
            }
            try
            {
                Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    if (_videoPlayerWindow?.MediaPlayer != null)
                    {
                        try
                        {
                            var currentTime = TimeSpan.FromMilliseconds(_videoPlayerWindow.MediaPlayer.Time);
                            var exactPosition = currentTime.TotalSeconds;
                            if (_introMuteSeconds.HasValue && exactPosition >= _introMuteSeconds.Value)
                            {
                                _videoPlayerWindow.MediaPlayer.Volume = ClampVolume(_baseVolume);
                                _introMuteSeconds = null;
                            }
                            if (_fadeStartTimeSeconds.HasValue && exactPosition >= _fadeStartTimeSeconds.Value)
                            {
                                var elapsed = exactPosition - _fadeStartTimeSeconds.Value;
                                var progress = Math.Clamp(elapsed / 7.0, 0, 1);
                                var newVol = _baseVolume * (1 - progress);
                                _videoPlayerWindow.MediaPlayer.Volume = ClampVolume(newVol);
                                if (elapsed >= 8.0)
                                {
                                    _fadeStartTimeSeconds = null;
                                    _manualFadeActive = false;
                                    _videoPlayerWindow.StopVideo();
                                    await HandleSongEnded();
                                    return;
                                }
                            }
                            var rounded = Math.Round(exactPosition);
                            if (Math.Abs(rounded - _lastPosition) >= 1.0)
                            {
                                CurrentVideoPosition = TimeSpan.FromSeconds(rounded).ToString(@"m\:ss");
                                SongPosition = rounded;
                                _lastPosition = rounded;
                                Log.Verbose("[DJSCREEN] Updated SongPosition to {Position}", rounded);
                                OnPropertyChanged(nameof(SongPosition));
                                OnPropertyChanged(nameof(CurrentVideoPosition));
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error("[DJSCREEN] Update timer error: {Message}", ex.Message);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to update position: {Message}", ex.Message);
            }
        }

        [RelayCommand]
        private void StartSeeking()
        {
            lock (_stateLock)
            {
                if (_isDisposing || _videoPlayerWindow?.MediaPlayer == null) return;
                _isSeeking = true;
                _wasPlaying = _videoPlayerWindow.MediaPlayer.IsPlaying || IsPlaying;
                if (_videoPlayerWindow.MediaPlayer.IsPlaying)
                {
                    _videoPlayerWindow.MediaPlayer.Pause();
                    Log.Information("[DJSCREEN] Paused video for seeking");
                }
                Log.Information("[DJSCREEN] Started seeking, WasPlaying={WasPlaying}, MediaState={State}", _wasPlaying, _videoPlayerWindow.MediaPlayer.State);
            }
        }

        [RelayCommand]
        private void StopSeeking()
        {
            lock (_stateLock)
            {
                if (_isDisposing || _videoPlayerWindow?.MediaPlayer == null) return;
                _isSeeking = false;
                if (_wasPlaying && _videoPlayerWindow.MediaPlayer.State != VLCState.Playing)
                {
                    _videoPlayerWindow.MediaPlayer.Play();
                    Log.Information("[DJSCREEN] Resumed video after seeking, MediaState={State}", _videoPlayerWindow.MediaPlayer.State);
                }
                Log.Information("[DJSCREEN] Stopped seeking");
            }
        }

        [RelayCommand]
        private void SeekSong(double position)
        {
            lock (_stateLock)
            {
                if (_isDisposing || _videoPlayerWindow?.MediaPlayer == null || _isInitialPlayback || _videoPlayerWindow.MediaPlayer.State == VLCState.Stopped)
                {
                    Log.Verbose("[DJSCREEN] SeekSong skipped: Disposing={Disposing}, MediaPlayer={MediaPlayer}, InitialPlayback={InitialPlayback}, State={State}",
                        _isDisposing, _videoPlayerWindow?.MediaPlayer != null, _isInitialPlayback, _videoPlayerWindow?.MediaPlayer?.State);
                    return;
                }
                double maxPosition = SongDuration.TotalSeconds;
                position = Math.Max(0, Math.Min(position, maxPosition));
                if (!_isSeeking)
                {
                    Log.Verbose("[DJSCREEN] SeekSong skipped: Not in seeking mode, Position={Position}, LastPosition={LastPosition}", position, _lastPosition);
                    return;
                }
                _pendingSeekPosition = position;
                if (_debounceTimer == null)
                {
                    _debounceTimer = new Timer(300);
                    _debounceTimer.Elapsed += DebounceTimer_Elapsed;
                    _debounceTimer.AutoReset = false;
                }
                _debounceTimer.Stop();
                _debounceTimer.Start();
                Log.Information("[DJSCREEN] SeekSong initiated: Position={Position}", position);
            }
        }

        private void DebounceTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            lock (_stateLock)
            {
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        if (_videoPlayerWindow?.MediaPlayer == null) return;
                        Log.Information("[DJSCREEN] Debounced seek to position: {Position}", _pendingSeekPosition);
                        _isSeeking = true;
                        _videoPlayerWindow.MediaPlayer.Pause();
                        long seekTime = (long)(_pendingSeekPosition * 1000);
                        _videoPlayerWindow.MediaPlayer.Time = seekTime;
                        _lastPosition = Math.Round(_pendingSeekPosition);
                        SongPosition = _lastPosition;
                        CurrentVideoPosition = TimeSpan.FromSeconds(_lastPosition).ToString(@"m\:ss");
                        OnPropertyChanged(nameof(SongPosition));
                        OnPropertyChanged(nameof(CurrentVideoPosition));
                        if (_wasPlaying && _videoPlayerWindow.MediaPlayer.State != VLCState.Playing)
                        {
                            Task.Delay(300).ContinueWith(_ =>
                            {
                                if (_videoPlayerWindow?.MediaPlayer != null)
                                {
                                    _videoPlayerWindow.MediaPlayer.Play();
                                    Log.Information("[DJSCREEN] Delayed resume after seek, MediaState={State}", _videoPlayerWindow.MediaPlayer.State);
                                }
                            });
                        }
                        _isSeeking = false;
                    }
                    catch (Exception ex)
                    {
                        Log.Error("[DJSCREEN] Failed to debounced seek: {Message}", ex.Message);
                        SetWarningMessage($"Failed to seek: {ex.Message}");
                        _isSeeking = false;
                    }
                });
            }
        }

        private void OnVLCPositionChanged(object? sender, MediaPlayerPositionChangedEventArgs e)
        {
            lock (_stateLock)
            {
                if (_isDisposing || _isSeeking || _isInitialPlayback) return;
                try
                {
                    Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            if (_videoPlayerWindow?.MediaPlayer == null) return;
                            var exactPosition = _videoPlayerWindow.MediaPlayer.Time / 1000.0;
                            var rounded = Math.Round(exactPosition);
                            if (Math.Abs(rounded - _lastPosition) >= 1.0)
                            {
                                SongPosition = rounded;
                                _lastPosition = rounded;
                                CurrentVideoPosition = TimeSpan.FromSeconds(rounded).ToString(@"m\:ss");
                                OnPropertyChanged(nameof(SongPosition));
                                OnPropertyChanged(nameof(CurrentVideoPosition));
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error("[DJSCREEN] PositionChanged error: {Message}", ex.Message);
                        }
                    });
                }
                catch (Exception ex)
                {
                    Log.Error("[DJSCREEN] Failed to handle VLC PositionChanged: {Message}", ex.Message);
                }
            }
        }

        private void OnVLCError(object? sender, EventArgs e)
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                SetWarningMessage("Playback error occurred in VLC.");
                Log.Error("[DJSCREEN] VLC playback error detected");
            });
        }

        private void NotifyAllProperties()
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                OnPropertyChanged(nameof(PlayingQueueEntry));
                OnPropertyChanged(nameof(SelectedQueueEntry));
                OnPropertyChanged(nameof(IsPlaying));
                OnPropertyChanged(nameof(IsVideoPaused));
                OnPropertyChanged(nameof(SongPosition));
                OnPropertyChanged(nameof(CurrentVideoPosition));
                OnPropertyChanged(nameof(TimeRemaining));
                OnPropertyChanged(nameof(TimeRemainingSeconds));
                OnPropertyChanged(nameof(StopRestartButtonColor));
                OnPropertyChanged(nameof(SongDuration));
                CommandManager.InvalidateRequerySuggested();
                Log.Information("[DJSCREEN] Notified all UI properties");
            });
        }

        private void ResetPlaybackState()
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsPlaying = false;
                IsVideoPaused = false;
                SongPosition = 0;
                _lastPosition = 0;
                _countdownStarted = false;
                _isInitialPlayback = false;
                CurrentVideoPosition = "--:--";
                TimeRemainingSeconds = 0;
                TimeRemaining = "--:--";
                StopRestartButtonColor = "#22d3ee";
                PlayingQueueEntry = null;
                SongDuration = TimeSpan.Zero;
                _totalDuration = null;
                _fadeStartTimeSeconds = null;
                _introMuteSeconds = null;
                _baseVolume = 100;
                _configuredFadeStartSeconds = null;
                _manualFadeActive = false;
                _fullSongDuration = null;
                NormalizationDisplay = "0.0";
                if (_updateTimer != null)
                {
                    _updateTimer.Tick -= UpdateTimer_Tick;
                    _updateTimer.Stop();
                    _updateTimer = null;
                    Log.Information("[DJSCREEN] Stopped update timer in ResetPlaybackState");
                }
                NotifyAllProperties();
                Log.Information("[DJSCREEN] Playback state reset");
            });
        }

        private void VideoPlayerWindow_MediaPlayerReinitialized(object? sender, EventArgs? e)
        {
            if (_isDisposing) return;
            if (_videoPlayerWindow?.MediaPlayer == null) return;

            AttachMediaPlayerHandlers(_videoPlayerWindow.MediaPlayer);
            _videoPlayerWindow.SetBassGain(BassBoost);
            _videoPlayerWindow.MediaPlayer.Volume = _introMuteSeconds.HasValue ? 0 : ClampVolume(_baseVolume);
        }

        private void VideoPlayerWindow_MediaLengthChanged(object? sender, long length)
        {
            if (length <= 0) return;
            var duration = TimeSpan.FromMilliseconds(length);
            if (_fullSongDuration.HasValue && Math.Abs((_fullSongDuration.Value - duration).TotalSeconds) < 0.5)
            {
                return;
            }
            _ = UpdateSongDurationAsync(duration, "LibVLC metadata");
        }

        private void AttachMediaPlayerHandlers(LibVLCSharp.Shared.MediaPlayer? player)
        {
            if (ReferenceEquals(_attachedMediaPlayer, player))
            {
                return;
            }

            if (_attachedMediaPlayer != null)
            {
                _attachedMediaPlayer.PositionChanged -= OnVLCPositionChanged;
                _attachedMediaPlayer.EncounteredError -= OnVLCError;
            }

            _attachedMediaPlayer = player;

            if (_attachedMediaPlayer != null)
            {
                _attachedMediaPlayer.PositionChanged += OnVLCPositionChanged;
                _attachedMediaPlayer.EncounteredError += OnVLCError;
            }
        }

        private void DetachMediaPlayerHandlers()
        {
            if (_attachedMediaPlayer != null)
            {
                _attachedMediaPlayer.PositionChanged -= OnVLCPositionChanged;
                _attachedMediaPlayer.EncounteredError -= OnVLCError;
                _attachedMediaPlayer = null;
            }
        }

        private async Task UpdateSongDurationAsync(TimeSpan duration, string source)
        {
            try
            {
                _fullSongDuration = duration;
                var effectiveSeconds = duration.TotalSeconds;
                double? fadeStart = null;

                if (_manualFadeActive && _fadeStartTimeSeconds.HasValue)
                {
                    fadeStart = _fadeStartTimeSeconds.Value;
                }
                else if (_configuredFadeStartSeconds.HasValue)
                {
                    fadeStart = _configuredFadeStartSeconds.Value;
                }
                else if (_fadeStartTimeSeconds.HasValue)
                {
                    fadeStart = _fadeStartTimeSeconds.Value;
                }

                if (fadeStart.HasValue && fadeStart.Value > 0 && fadeStart.Value + 8 < effectiveSeconds)
                {
                    effectiveSeconds = fadeStart.Value + 8;
                }

                if (_manualFadeActive && _fadeStartTimeSeconds.HasValue)
                {
                    effectiveSeconds = Math.Min(effectiveSeconds, _fadeStartTimeSeconds.Value + 8);
                }

                _totalDuration = TimeSpan.FromSeconds(Math.Max(effectiveSeconds, 0));

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    SongDuration = duration;
                    var currentSeconds = _videoPlayerWindow?.MediaPlayer != null
                        ? _videoPlayerWindow.MediaPlayer.Time / 1000.0
                        : 0;
                    var remaining = Math.Max(0, _totalDuration.Value.TotalSeconds - currentSeconds);
                    TimeRemainingSeconds = (int)Math.Ceiling(remaining);
                    TimeRemaining = _totalDuration.Value.ToString(@"m\:ss");
                    OnPropertyChanged(nameof(SongDuration));
                    OnPropertyChanged(nameof(TimeRemaining));
                    OnPropertyChanged(nameof(TimeRemainingSeconds));
                });

                Log.Information("[DJSCREEN] Updated song duration from {Source}: Full={Full}, Effective={Effective}", source, duration, _totalDuration);
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to update song duration from {Source}: {Message}", source, ex.Message);
            }
        }

        private async Task UpdateSongDurationForManualFadeAsync(double fadeStartSeconds)
        {
            var duration = _fullSongDuration ?? TimeSpan.FromSeconds(Math.Max(fadeStartSeconds + 8, fadeStartSeconds));
            await UpdateSongDurationAsync(duration, "Manual fade");
        }

        private static double CalculateBaseVolume(float? gain)
        {
            var baseVolume = 100.0;
            if (gain.HasValue)
            {
                baseVolume *= Math.Pow(10, gain.Value / 20.0);
            }
            return Math.Clamp(baseVolume, 0, 200);
        }

        private static int ClampVolume(double volume)
        {
            return (int)Math.Clamp(Math.Round(volume), 0, 200);
        }

        private static string FormatNormalization(float? gain)
        {
            if (!gain.HasValue || Math.Abs(gain.Value) < 0.05f)
            {
                return "0.0";
            }

            return gain.Value >= 0 ? $"+{gain.Value:0.0}" : $"{gain.Value:0.0}";
        }

        private static bool TryParseSongDuration(string? input, out TimeSpan duration)
        {
            duration = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            var formats = new[]
            {
                @"m\:ss",
                @"mm\:ss",
                @"h\:mm\:ss",
                @"hh\:mm\:ss",
                @"m\:ss\.fff",
                @"mm\:ss\.fff"
            };

            foreach (var format in formats)
            {
                if (TimeSpan.TryParseExact(input, format, CultureInfo.InvariantCulture, out duration))
                {
                    return true;
                }
            }

            return TimeSpan.TryParse(input, CultureInfo.InvariantCulture, out duration);
        }

        [RelayCommand]
        private async Task ToggleShow()
        {
            Log.Information("[DJSCREEN] ToggleShow command invoked");
            if (_isDisposing) return;

            try
            {
                if (IsShowActive)
                {
                    Log.Information("[DJSCREEN] Ending show");
                    CurrentShowState = ShowState.Ended;
                    TeardownShowVisuals();
                    SetPreShowButton();
                    CurrentShowState = ShowState.PreShow;
                    Log.Information("[DJSCREEN] Show visuals torn down");
                    return;
                }

                if (IsShowActive)
                {
                    Log.Information("[DJSCREEN] Start show requested but visuals already active");
                    return;
                }

                if (string.IsNullOrEmpty(_currentEventId))
                {
                    Log.Information("[DJSCREEN] ToggleShow failed: No event joined");
                    await SetWarningMessageAsync("Please join an event before starting the show.");
                    return;
                }

                Log.Information("[DJSCREEN] Starting show");
                if (!InitializeShowVisuals(out var initializationError))
                {
                    var warning = string.IsNullOrWhiteSpace(initializationError)
                        ? "Video player initialization failed."
                        : initializationError!;
                    Log.Error("[DJSCREEN] Failed to start show visuals: {Message}", warning);
                    await SetWarningMessageAsync($"Failed to start show: {warning}");
                    return;
                }

                SetLiveShowButton();
                CurrentShowState = ShowState.Running;
                NotifyAllProperties();
                Log.Information("[DJSCREEN] Show started, VideoPlayerWindow shown with idle title");
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to toggle show: {Message}", ex.Message);
                await SetWarningMessageAsync($"Failed to toggle show: {ex.Message}");
                CurrentShowState = ShowState.Ended;
                TeardownShowVisuals();
                SetPreShowButton();
                CurrentShowState = ShowState.PreShow;
            }
        }

        [RelayCommand(CanExecute = nameof(CanRestartAudioEngine))]
        private void RestartAudioEngine()
        {
            if (_videoPlayerWindow == null)
            {
                SetWarningMessage("Video player is not ready. Start the show before restarting audio.");
                return;
            }

            try
            {
                _videoPlayerWindow.RestartAudioEngine();
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to restart audio engine: {Message}", ex.Message);
                SetWarningMessage($"Failed to restart audio engine: {ex.Message}");
            }
        }

        private bool CanRestartAudioEngine()
        {
            return _videoPlayerWindow != null && _settingsService.Settings.EnableAudioEngineRestartButton;
        }

        [RelayCommand(CanExecute = nameof(CanPlayTestTone))]
        private void PlayTestTone()
        {
            if (_videoPlayerWindow == null)
            {
                SetWarningMessage("Video player is not ready. Start the show before playing a test tone.");
                return;
            }

            if (CurrentShowState is ShowState.PreShow or ShowState.Ended)
            {
                SetWarningMessage("Start the show before playing a test tone.");
                return;
            }

            try
            {
                _videoPlayerWindow.PlayTestTone();
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to play test tone: {Message}", ex.Message);
                SetWarningMessage($"Failed to play test tone: {ex.Message}");
            }
        }

        private bool CanPlayTestTone()
        {
            return _videoPlayerWindow != null
                && CurrentShowState is ShowState.Running or ShowState.Paused
                && !IsPlaying
                && !IsVideoPaused;
        }

        [RelayCommand]
        private async Task PlayAsync()
        {
            Log.Information("[DJSCREEN] Play/Pause command invoked");
            if (_isDisposing)
            {
                Log.Information("[DJSCREEN] Play failed: ViewModel is disposing");
                await Task.CompletedTask;
                return;
            }
            if (!IsShowActive)
            {
                Log.Information("[DJSCREEN] Play failed: Show not started");
                await SetWarningMessageAsync("Please start the show first.");
                return;
            }

            if (QueueEntries.Count == 0)
            {
                Log.Information("[DJSCREEN] Play failed: Queue is empty");
                await SetWarningMessageAsync("No songs in the queue.");
                return;
            }

            if (!EnsureShowVisualsReady(out var visualsError))
            {
                Log.Information("[DJSCREEN] Play failed: Show visuals unavailable");
                var message = string.IsNullOrWhiteSpace(visualsError)
                    ? "Video player not initialized. Please restart the show."
                    : visualsError!;
                await SetWarningMessageAsync(message);
                return;
            }

            if (_videoPlayerWindow?.MediaPlayer == null)
            {
                Log.Information("[DJSCREEN] Play failed: Video player not initialized");
                await SetWarningMessageAsync("Video player not initialized. Please restart the show.");
                return;
            }

            if (IsVideoPaused && PlayingQueueEntry != null && _videoPlayerWindow.MediaPlayer != null)
            {
                try
                {
                    Log.Information("[DJSCREEN] Resuming paused video: QueueId={QueueId}", PlayingQueueEntry.QueueId);
                    _videoPlayerWindow.MediaPlayer.Play();
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        IsVideoPaused = false;
                        IsPlaying = true;
                        CurrentShowState = ShowState.Running;
                        StopRestartButtonColor = "#22d3ee";
                        NotifyAllProperties();
                        Log.Information("[DJSCREEN] UI updated for resume: QueueId={QueueId}, SongTitle={SongTitle}", PlayingQueueEntry.QueueId, PlayingQueueEntry.SongTitle);
                    });
                    Log.Information("[DJSCREEN] Resumed video for event {EventId}, queue {QueueId}: {SongTitle}", _currentEventId, PlayingQueueEntry.QueueId, PlayingQueueEntry.SongTitle);
                    if (!string.IsNullOrEmpty(_currentEventId))
                    {
                        await _apiService.PlayAsync(_currentEventId, PlayingQueueEntry.QueueId.ToString());
                        Log.Information("[DJSCREEN] Play request sent for event {EventId}, queue {QueueId}: {SongTitle}", _currentEventId, PlayingQueueEntry.QueueId, PlayingQueueEntry.SongTitle);
                    }
                    if (_updateTimer == null)
                    {
                        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                        _updateTimer.Tick += UpdateTimer_Tick;
                        _updateTimer.Start();
                        Log.Information("[DJSCREEN] Started update timer for resume");
                    }
                    return;
                }
                catch (Exception ex)
                {
                    Log.Error("[DJSCREEN] Failed to resume queue {QueueId}: {Message}", PlayingQueueEntry.QueueId, ex.Message);
                    await SetWarningMessageAsync($"Failed to resume: {ex.Message}");
                    return;
                }
            }

            if (IsPlaying && _videoPlayerWindow?.MediaPlayer != null)
            {
                try
                {
                    Log.Information("[DJSCREEN] Pausing video: QueueId={QueueId}", PlayingQueueEntry?.QueueId ?? -1);
                    _videoPlayerWindow.PauseVideo();
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        IsVideoPaused = true;
                        IsPlaying = false;
                        CurrentShowState = ShowState.Paused;
                        StopRestartButtonColor = "#FF0000";
                        NotifyAllProperties();
                        Log.Information("[DJSCREEN] UI updated for pause: QueueId={QueueId}, SongTitle={SongTitle}", PlayingQueueEntry?.QueueId ?? -1, PlayingQueueEntry?.SongTitle ?? "Unknown");
                    });
                    if (!string.IsNullOrEmpty(_currentEventId) && PlayingQueueEntry != null)
                    {
                        await _apiService.PauseAsync(_currentEventId, PlayingQueueEntry.QueueId.ToString());
                        Log.Information("[DJSCREEN] Pause request sent for event {EventId}, queue {QueueId}: {SongTitle}", _currentEventId, PlayingQueueEntry.QueueId, PlayingQueueEntry.SongTitle);
                    }
                    return;
                }
                catch (Exception ex)
                {
                    Log.Error("[DJSCREEN] Failed to pause queue {QueueId}: {Message}", PlayingQueueEntry?.QueueId ?? -1, ex.Message);
                    await SetWarningMessageAsync($"Failed to pause: {ex.Message}");
                    return;
                }
            }

            if (string.IsNullOrEmpty(_currentEventId))
            {
                Log.Information("[DJSCREEN] Play failed: No event joined");
                await SetWarningMessageAsync("Please join an event.");
                return;
            }

            try
            {
                await LoadQueueData();

                QueueEntry? targetEntry = null;
                lock (_queueLock)
                {
                    Log.Information("[DJSCREEN] Selecting target entry for auto-play");
                    Log.Information("[DJSCREEN] SelectedQueueEntry: QueueId={QueueId}, SongTitle={SongTitle}, IsUpNext={IsUpNext}, IsSingerJoined={IsSingerJoined}, IsSingerOnBreak={IsSingerOnBreak}, IsActive={IsActive}, IsOnHold={IsOnHold}, IsVideoCached={IsVideoCached}",
                        SelectedQueueEntry?.QueueId ?? -1, SelectedQueueEntry?.SongTitle ?? "null", SelectedQueueEntry?.IsUpNext ?? false, SelectedQueueEntry?.IsSingerJoined ?? false, SelectedQueueEntry?.IsSingerOnBreak ?? false, SelectedQueueEntry?.IsActive ?? false, SelectedQueueEntry?.IsOnHold ?? false, SelectedQueueEntry?.IsVideoCached ?? false);

                    foreach (var entry in QueueEntries)
                    {
                        Log.Information("[DJSCREEN] Queue Entry: QueueId={QueueId}, SongTitle={SongTitle}, IsUpNext={IsUpNext}, IsActive={IsActive}, IsOnHold={IsOnHold}, IsVideoCached={IsVideoCached}, IsSingerLoggedIn={IsSingerLoggedIn}, IsSingerJoined={IsSingerJoined}, IsSingerOnBreak={IsSingerOnBreak}",
                            entry.QueueId, entry.SongTitle, entry.IsUpNext, entry.IsActive, entry.IsOnHold, entry.IsVideoCached, entry.IsSingerLoggedIn, entry.IsSingerJoined, entry.IsSingerOnBreak);
                    }

                    bool selectedEntryInvalid = SelectedQueueEntry != null &&
                        (!SelectedQueueEntry.IsSingerJoined || SelectedQueueEntry.IsSingerOnBreak || !SelectedQueueEntry.IsActive || !SelectedQueueEntry.IsVideoCached ||
                        (IsAutoPlayEnabled && SelectedQueueEntry.IsOnHold));

                    if (selectedEntryInvalid)
                    {
                        Log.Information("[DJSCREEN] Invalid SelectedQueueEntry for auto-play, resetting to null: QueueId={QueueId}, SongTitle={SongTitle}", SelectedQueueEntry.QueueId, SelectedQueueEntry.SongTitle);
                        SelectedQueueEntry = null;
                        OnPropertyChanged(nameof(SelectedQueueEntry));
                    }

                    bool AutoplayEligible(QueueEntry q) => q.IsActive && q.IsVideoCached && q.IsSingerLoggedIn && q.IsSingerJoined && !q.IsSingerOnBreak && (!IsAutoPlayEnabled || !q.IsOnHold);

                    targetEntry = QueueEntries.FirstOrDefault(q => q.IsUpNext && AutoplayEligible(q)) ??
                                  QueueEntries.FirstOrDefault(AutoplayEligible);

                    if (targetEntry == null && SelectedQueueEntry != null && (!IsAutoPlayEnabled || !SelectedQueueEntry.IsOnHold))
                    {
                        Log.Information("[DJSCREEN] Falling back to SelectedQueueEntry: QueueId={QueueId}, SongTitle={SongTitle}", SelectedQueueEntry.QueueId, SelectedQueueEntry.SongTitle);
                        targetEntry = SelectedQueueEntry;
                    }

                    if (targetEntry == null && !IsAutoPlayEnabled)
                    {
                        targetEntry = SelectedQueueEntry ?? QueueEntries.FirstOrDefault();
                    }
                }

                if (targetEntry == null)
                {
                    Log.Information("[DJSCREEN] Play failed: No valid green singer available. SelectedQueueEntry={Selected}, UpNextCount={UpNextCount}, GreenSingerCount={GreenCount}",
                        SelectedQueueEntry?.QueueId ?? -1, QueueEntries.Count(q => q.IsUpNext), QueueEntries.Count(q => q.IsActive && q.IsVideoCached && q.IsSingerLoggedIn && q.IsSingerJoined && !q.IsSingerOnBreak && (!IsAutoPlayEnabled || !q.IsOnHold)));
                    await SetWarningMessageAsync("No valid green singers available to play.");
                    return;
                }

                Log.Information("[DJSCREEN] Selected target entry for play: QueueId={QueueId}, SongTitle={SongTitle}, IsUpNext={IsUpNext}, IsActive={IsActive}, IsOnHold={IsOnHold}, IsVideoCached={IsVideoCached}, IsSingerLoggedIn={IsSingerLoggedIn}, IsSingerJoined={IsSingerJoined}, IsSingerOnBreak={IsSingerOnBreak}",
                    targetEntry.QueueId, targetEntry.SongTitle, targetEntry.IsUpNext, targetEntry.IsActive, targetEntry.IsOnHold, targetEntry.IsVideoCached, targetEntry.IsSingerLoggedIn, targetEntry.IsSingerJoined, targetEntry.IsSingerOnBreak);

                if (!EnsureShowVisualsReady(out var entryVisualsError))
                {
                    var message = string.IsNullOrWhiteSpace(entryVisualsError)
                        ? "Failed to initialize video player. Please restart the show."
                        : entryVisualsError!;
                    Log.Error("[DJSCREEN] Unable to prepare show visuals for QueueId={QueueId}: {Message}", targetEntry.QueueId, message);
                    await SetWarningMessageAsync(message);
                    return;
                }

                AttachMediaPlayerHandlers(_videoPlayerWindow?.MediaPlayer);

                if (_videoPlayerWindow?.MediaPlayer == null)
                {
                    Log.Error("[DJSCREEN] Video player not available for QueueId={QueueId}", targetEntry.QueueId);
                    await SetWarningMessageAsync("Failed to play: Video player unavailable. Please restart the show.");
                    return;
                }

                string videoPath = Path.Combine(_settingsService.Settings.VideoCachePath, $"{targetEntry.SongId}.mp4");
                Log.Information("[DJSCREEN] Video path for QueueId={QueueId}: {Path}", targetEntry.QueueId, videoPath);

                if (!File.Exists(videoPath))
                {
                    Log.Error("[DJSCREEN] Video file not found for QueueId={QueueId}: {Path}", targetEntry.QueueId, videoPath);
                    await SetWarningMessageAsync($"Video file not found: {videoPath}");
                    return;
                }

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    PlayingQueueEntry = targetEntry;
                    SelectedQueueEntry = targetEntry;
                    IsPlaying = true;
                    IsVideoPaused = false;
                    CurrentShowState = ShowState.Running;
                    SongPosition = 0;
                    _lastPosition = 0;
                    CurrentVideoPosition = "0:00";
                    TimeRemainingSeconds = 0;
                    TimeRemaining = "--:--";
                    StopRestartButtonColor = "#22d3ee";
                    NormalizationDisplay = FormatNormalization(targetEntry.NormalizationGain);
                    NotifyAllProperties();
                    Log.Information("[DJSCREEN] UI updated for new song: QueueId={QueueId}, SongTitle={SongTitle}", targetEntry.QueueId, targetEntry.SongTitle);
                });

                _isInitialPlayback = true;
                try
                {
                    Log.Information("[DJSCREEN] Attempting to play video for QueueId={QueueId}, Path={Path}", targetEntry.QueueId, videoPath);
                    _videoPlayerWindow.PlayVideo(videoPath);
                    _videoPlayerWindow.ShowWindow();
                    _baseVolume = CalculateBaseVolume(targetEntry.NormalizationGain);
                    _configuredFadeStartSeconds = (targetEntry.FadeStartTime.HasValue && targetEntry.FadeStartTime.Value > 0)
                        ? targetEntry.FadeStartTime.Value
                        : null;
                    _fadeStartTimeSeconds = _configuredFadeStartSeconds;
                    _manualFadeActive = false;
                    _fullSongDuration = null;
                    _introMuteSeconds = (targetEntry.IntroMuteDuration.HasValue && targetEntry.IntroMuteDuration.Value > 0)
                        ? targetEntry.IntroMuteDuration.Value
                        : null;
                    _videoPlayerWindow.MediaPlayer.Volume = _introMuteSeconds.HasValue ? 0 : ClampVolume(_baseVolume);
                    Log.Information("[VIDEO PLAYER] Video playback started for QueueId={QueueId}, Path={Path}", targetEntry.QueueId, videoPath);
                }
                catch (Exception ex)
                {
                    Log.Error("[DJSCREEN] Failed to start video playback for QueueId={QueueId}: {Message}", targetEntry.QueueId, ex.Message);
                    await SetWarningMessageAsync($"Failed to play video: {ex.Message}");
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        IsPlaying = false;
                        IsVideoPaused = true;
                        NotifyAllProperties();
                        Log.Information("[DJSCREEN] UI reset after playback failure for QueueId={QueueId}", targetEntry.QueueId);
                    });
                    _isInitialPlayback = false;
                    return;
                }
                finally
                {
                    _isInitialPlayback = false;
                }

                if (TryParseSongDuration(targetEntry.VideoLength, out var duration))
                {
                    await UpdateSongDurationAsync(duration, "Queue metadata");
                }
                else
                {
                    Log.Warning("[DJSCREEN] Failed to parse VideoLength for QueueId={QueueId}, VideoLength={VideoLength}", targetEntry.QueueId, targetEntry.VideoLength);
                }

                try
                {
                    await _apiService.PlayAsync(_currentEventId!, targetEntry.QueueId.ToString());
                    Log.Information("[DJSCREEN] Play request sent for event {EventId}, queue {QueueId}: {SongTitle}", _currentEventId, targetEntry.QueueId, targetEntry.SongTitle);
                }
                catch (Exception ex)
                {
                    Log.Error("[DJSCREEN] Failed to send play request for QueueId={QueueId}: {Message}", targetEntry.QueueId, ex.Message);
                    await SetWarningMessageAsync($"Failed to notify server: {ex.Message}");
                }

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    QueueEntries.Remove(targetEntry);
                    for (int i = 0; i < QueueEntries.Count; i++)
                    {
                        QueueEntries[i].Position = i + 1;
                    }
                    OnPropertyChanged(nameof(QueueEntries));
                    Log.Information("[DJSCREEN] Removed played song from queue: QueueId={QueueId}, SongTitle={SongTitle}", targetEntry.QueueId, targetEntry.SongTitle);
                });

                if (_countdownTimer == null)
                {
                    _countdownTimer = new Timer(2000);
                    _countdownTimer.Elapsed += CountdownTimer_Elapsed;
                    _countdownTimer.Start();
                }
                if (_updateTimer == null)
                {
                    _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                    _updateTimer.Tick += UpdateTimer_Tick;
                    _updateTimer.Start();
                    Log.Information("[DJSCREEN] Started update timer for QueueId={QueueId}", targetEntry.QueueId);
                }

                if (QueueEntries.Count <= 20)
                {
                    await UpdateQueueColorsAndRules();
                }
                else
                {
                    Log.Information("[DJSCREEN] Deferred UpdateQueueColorsAndRules due to large queue size: {Count}", QueueEntries.Count);
                    var queueUpdateTask = UpdateQueueColorsAndRules(); // Assign to suppress CS4014
                }
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to play queue: {Message}", ex.Message);
                await SetWarningMessageAsync($"Failed to play: {ex.Message}");
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    IsPlaying = false;
                    IsVideoPaused = true;
                    CurrentShowState = ShowState.Paused;
                    NotifyAllProperties();
                    Log.Information("[DJSCREEN] UI reset after playback failure");
                });
                _isInitialPlayback = false;
            }
        }

        [RelayCommand]
        private async Task StopRestartAsync()
        {
            Log.Information("[DJSCREEN] Stop/Restart command invoked");
            if (_isDisposing || _videoPlayerWindow?.MediaPlayer == null || PlayingQueueEntry == null)
            {
                return;
            }

            try
            {
                if (!IsVideoPaused)
                {
                    _videoPlayerWindow.StopVideo();
                    if (_updateTimer != null)
                    {
                        _updateTimer.Stop();
                    }
                    if (!string.IsNullOrEmpty(_currentEventId))
                    {
                        await _apiService.StopAsync(_currentEventId, PlayingQueueEntry.QueueId.ToString());
                    }
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        IsPlaying = false;
                        IsVideoPaused = true;
                        CurrentShowState = ShowState.Paused;
                        SongPosition = 0;
                        _lastPosition = 0;
                        CurrentVideoPosition = "0:00";
                        StopRestartButtonColor = "#FF0000";
                        OnPropertyChanged(nameof(SongPosition));
                        OnPropertyChanged(nameof(CurrentVideoPosition));
                        NotifyAllProperties();
                    });
                }
                else
                {
                    if (_videoPlayerWindow.MediaPlayer != null)
                    {
                        _baseVolume = 100;
                        if (PlayingQueueEntry.NormalizationGain.HasValue)
                        {
                            _baseVolume = 100 * Math.Pow(10, PlayingQueueEntry.NormalizationGain.Value / 20.0);
                        }

                        _fadeStartTimeSeconds = (PlayingQueueEntry.FadeStartTime.HasValue && PlayingQueueEntry.FadeStartTime.Value > 0)
                            ? PlayingQueueEntry.FadeStartTime.Value
                            : null;

                        _introMuteSeconds = (PlayingQueueEntry.IntroMuteDuration.HasValue && PlayingQueueEntry.IntroMuteDuration.Value > 0)
                            ? PlayingQueueEntry.IntroMuteDuration.Value
                            : null;

                        _videoPlayerWindow.MediaPlayer.Volume = _introMuteSeconds.HasValue ? 0 : (int)_baseVolume;
                    }

                    _videoPlayerWindow.RestartVideo();
                    if (_updateTimer == null)
                    {
                        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                        _updateTimer.Tick += UpdateTimer_Tick;
                        _updateTimer.Start();
                    }
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        IsVideoPaused = false;
                        IsPlaying = true;
                        CurrentShowState = ShowState.Running;
                        SongPosition = 0;
                        _lastPosition = 0;
                        CurrentVideoPosition = "0:00";
                        StopRestartButtonColor = "#22d3ee";
                        OnPropertyChanged(nameof(SongPosition));
                        OnPropertyChanged(nameof(CurrentVideoPosition));
                        NotifyAllProperties();
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to stop/restart song: {Message}", ex.Message);
                await SetWarningMessageAsync($"Failed to stop/restart: {ex.Message}");
            }
        }

        private async Task StopSongIfPlayingAsync()
        {
            if (_videoPlayerWindow?.MediaPlayer == null || !IsPlaying)
            {
                return;
            }

            try
            {
                Log.Information("[DJSCREEN] StopSongIfPlayingAsync invoked: QueueId={QueueId}", PlayingQueueEntry?.QueueId);
                _videoPlayerWindow.StopVideo();
                if (_updateTimer != null)
                {
                    _updateTimer.Stop();
                }

                if (!string.IsNullOrEmpty(_currentEventId) && PlayingQueueEntry != null)
                {
                    try
                    {
                        await _apiService.StopAsync(_currentEventId, PlayingQueueEntry.QueueId.ToString());
                        Log.Information("[DJSCREEN] StopSongIfPlayingAsync reported stop to API for QueueId={QueueId}", PlayingQueueEntry.QueueId);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("[DJSCREEN] Failed to notify API about stop for QueueId={QueueId}: {Message}, StackTrace={StackTrace}",
                            PlayingQueueEntry.QueueId, ex.Message, ex.StackTrace);
                    }
                }

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    IsPlaying = false;
                    IsVideoPaused = true;
                    CurrentShowState = ShowState.Paused;
                    SongPosition = 0;
                    _lastPosition = 0;
                    CurrentVideoPosition = "0:00";
                    StopRestartButtonColor = "#FF0000";
                    OnPropertyChanged(nameof(SongPosition));
                    OnPropertyChanged(nameof(CurrentVideoPosition));
                    NotifyAllProperties();
                });
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to stop song during exit workflow: {Message}, StackTrace={StackTrace}", ex.Message, ex.StackTrace);
            }
        }

        [RelayCommand]
        public async Task PlayQueueEntryAsync(QueueEntry? entry)
        {
            await PlayQueueEntryInternalAsync(entry, true);
        }

        [RelayCommand]
        private async Task TriggerManualFadeOutAsync()
        {
            if (_isDisposing)
            {
                Log.Information("[DJSCREEN] Manual fade skipped: disposing");
                return;
            }

            if (_videoPlayerWindow?.MediaPlayer == null || !IsPlaying || !_videoPlayerWindow.MediaPlayer.IsPlaying)
            {
                Log.Information("[DJSCREEN] Manual fade skipped: no active playback");
                return;
            }

            try
            {
                var currentSeconds = _videoPlayerWindow.MediaPlayer.Time / 1000.0;
                Log.Information("[DJSCREEN] Manual fade triggered at {Seconds}s", currentSeconds);

                if (_manualFadeActive && _fadeStartTimeSeconds.HasValue &&
                    Math.Abs(currentSeconds - _fadeStartTimeSeconds.Value) < 0.5)
                {
                    Log.Information("[DJSCREEN] Manual fade already in progress");
                    return;
                }

                _manualFadeActive = true;
                _fadeStartTimeSeconds = currentSeconds;

                if (_introMuteSeconds.HasValue && currentSeconds < _introMuteSeconds.Value)
                {
                    _introMuteSeconds = null;
                    _videoPlayerWindow.MediaPlayer.Volume = ClampVolume(_baseVolume);
                }

                await UpdateSongDurationForManualFadeAsync(currentSeconds);
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to trigger manual fade: {Message}", ex.Message);
                await SetWarningMessageAsync($"Failed to fade out: {ex.Message}");
            }
        }

        private async Task PlayQueueEntryInternalAsync(QueueEntry? entry, bool requireConfirmation)
        {
            Log.Information("[DJSCREEN] PlayQueueEntryAsync invoked for QueueId={QueueId}, SongTitle={SongTitle}, IsSingerOnBreak={IsSingerOnBreak}",
                entry?.QueueId ?? -1, entry?.SongTitle ?? "Unknown", entry?.IsSingerOnBreak ?? false);

            if (_isDisposing)
            {
                Log.Information("[DJSCREEN] PlayQueueEntryAsync skipped: ViewModel is disposing");
                await Task.CompletedTask;
                return;
            }
            if (entry == null)
            {
                Log.Information("[DJSCREEN] PlayQueueEntryAsync skipped: Entry is null");
                await SetWarningMessageAsync("No song selected to play.");
                return;
            }
            if (!IsShowActive)
            {
                Log.Information("[DJSCREEN] PlayQueueEntry failed: Show not started");
                await SetWarningMessageAsync("Please start the show first.");
                return;
            }
            if (_videoPlayerWindow == null || _videoPlayerWindow.MediaPlayer == null)
            {
                Log.Information("[DJSCREEN] PlayQueueEntry failed: Video player not initialized");
                await SetWarningMessageAsync("Video player not initialized. Please restart the show.");
                return;
            }

            try
            {
                if (requireConfirmation)
                {
                    string confirmMessage = (IsPlaying || IsVideoPaused)
                        ? $"Play '{entry.SongTitle ?? "Unknown"}' by {entry.RequestorDisplayName ?? "Unknown"} now? This will stop the current song."
                        : $"Play '{entry.SongTitle ?? "Unknown"}' by {entry.RequestorDisplayName ?? "Unknown"} now?";
                    var result = await Application.Current.Dispatcher.InvokeAsync(() =>
                        MessageBox.Show(confirmMessage, "Confirm Song Playback", MessageBoxButton.YesNo, MessageBoxImage.None));
                    Log.Information("[DJSCREEN] Confirmation dialog result for QueueId={QueueId}: {Result}", entry.QueueId, result);

                    if (result != MessageBoxResult.Yes)
                    {
                        Log.Information("[DJSCREEN] Playback of QueueId={QueueId} cancelled by user", entry.QueueId);
                        await Task.CompletedTask;
                        return;
                    }
                }

                if ((IsPlaying || IsVideoPaused) && PlayingQueueEntry != null)
                {
                    try
                    {
                        Log.Information("[DJSCREEN] Stopping current song for QueueId={QueueId}", PlayingQueueEntry.QueueId);
                        _videoPlayerWindow.PauseVideo();
                        Log.Information("[DJSCREEN] Paused video for QueueId={QueueId}", PlayingQueueEntry.QueueId);
                        if (!string.IsNullOrEmpty(_currentEventId))
                        {
                            await _apiService.PauseAsync(_currentEventId, PlayingQueueEntry.QueueId.ToString());
                            Log.Information("[DJSCREEN] Pause request sent for event {EventId}, queue {QueueId}: {SongTitle}", _currentEventId, PlayingQueueEntry.QueueId, PlayingQueueEntry.SongTitle);
                        }
                        _videoPlayerWindow.StopVideo();
                        Log.Information("[DJSCREEN] Video playback stopped for QueueId={QueueId}", PlayingQueueEntry.QueueId);
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            IsPlaying = false;
                            IsVideoPaused = true;
                            CurrentShowState = ShowState.Paused;
                            StopRestartButtonColor = "#FF0000";
                            NotifyAllProperties();
                            Log.Information("[DJSCREEN] UI updated after stopping song for QueueId={QueueId}", entry.QueueId);
                        });
                        if (_updateTimer != null)
                        {
                            _updateTimer.Stop();
                            Log.Information("[DJSCREEN] Stopped update timer for QueueId={QueueId}", entry.QueueId);
                        }
                        Log.Information("[DJSCREEN] Stopped current song to play new QueueId={QueueId}", entry.QueueId);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("[DJSCREEN] Failed to stop current song for QueueId={QueueId}: {Message}", PlayingQueueEntry?.QueueId ?? -1, ex.Message);
                        await SetWarningMessageAsync($"Failed to stop current song: {ex.Message}");
                        return;
                    }
                }

                if (string.IsNullOrEmpty(_currentEventId))
                {
                    Log.Information("[DJSCREEN] PlayQueueEntry failed: No event joined");
                    await SetWarningMessageAsync("Please join an event.");
                    return;
                }

                Log.Information("[DJSCREEN] Starting playback setup for QueueId={QueueId}", entry.QueueId);
                QueueEntry targetEntry = entry;
                Log.Information("[DJSCREEN] Target entry selected: QueueId={QueueId}, SongTitle={SongTitle}, IsSingerOnBreak={IsSingerOnBreak}",
                    targetEntry.QueueId, targetEntry.SongTitle, targetEntry.IsSingerOnBreak);

                if (!EnsureShowVisualsReady(out var visualsError))
                {
                    var message = string.IsNullOrWhiteSpace(visualsError)
                        ? "Failed to initialize video player. Please restart the show."
                        : visualsError!;
                    Log.Error("[DJSCREEN] Unable to prepare show visuals for QueueId={QueueId}: {Message}", targetEntry.QueueId, message);
                    await SetWarningMessageAsync(message);
                    return;
                }

                AttachMediaPlayerHandlers(_videoPlayerWindow?.MediaPlayer);

                if (_videoPlayerWindow?.MediaPlayer == null)
                {
                    Log.Error("[DJSCREEN] Video player not available for QueueId={QueueId}", targetEntry.QueueId);
                    await SetWarningMessageAsync("Failed to play: Video player unavailable. Please restart the show.");
                    return;
                }

                string videoPath = Path.Combine(_settingsService.Settings.VideoCachePath, $"{targetEntry.SongId}.mp4");
                Log.Information("[DJSCREEN] Video path for QueueId={QueueId}: {Path}", targetEntry.QueueId, videoPath);

                if (!File.Exists(videoPath))
                {
                    Log.Error("[DJSCREEN] Video file not found for QueueId={QueueId}: {Path}", targetEntry.QueueId, videoPath);
                    await SetWarningMessageAsync($"Video file not found: {videoPath}");
                    return;
                }

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    PlayingQueueEntry = targetEntry;
                    SelectedQueueEntry = targetEntry;
                    IsPlaying = true;
                    IsVideoPaused = false;
                    CurrentShowState = ShowState.Running;
                    SongPosition = 0;
                    _lastPosition = 0;
                    CurrentVideoPosition = "0:00";
                    TimeRemainingSeconds = 0;
                    TimeRemaining = "--:--";
                    StopRestartButtonColor = "#22d3ee";
                    NormalizationDisplay = FormatNormalization(targetEntry.NormalizationGain);
                    NotifyAllProperties();
                    Log.Information("[DJSCREEN] UI updated for new song: QueueId={QueueId}, SongTitle={SongTitle}", targetEntry.QueueId, targetEntry.SongTitle);
                });

                _isInitialPlayback = true;
                try
                {
                    Log.Information("[DJSCREEN] Attempting to play video for QueueId={QueueId}, Path={Path}", targetEntry.QueueId, videoPath);
                    _videoPlayerWindow.PlayVideo(videoPath);
                    _videoPlayerWindow.ShowWindow();
                    _baseVolume = CalculateBaseVolume(targetEntry.NormalizationGain);
                    _configuredFadeStartSeconds = (targetEntry.FadeStartTime.HasValue && targetEntry.FadeStartTime.Value > 0)
                        ? targetEntry.FadeStartTime.Value
                        : null;
                    _fadeStartTimeSeconds = _configuredFadeStartSeconds;
                    _manualFadeActive = false;
                    _fullSongDuration = null;
                    _introMuteSeconds = (targetEntry.IntroMuteDuration.HasValue && targetEntry.IntroMuteDuration.Value > 0)
                        ? targetEntry.IntroMuteDuration.Value
                        : null;
                    _videoPlayerWindow.MediaPlayer.Volume = _introMuteSeconds.HasValue ? 0 : ClampVolume(_baseVolume);
                    Log.Information("[VIDEO PLAYER] Video playback started for QueueId={QueueId}, Path={Path}", targetEntry.QueueId, videoPath);
                }
                catch (Exception ex)
                {
                    Log.Error("[DJSCREEN] Failed to start video playback for QueueId={QueueId}: {Message}", targetEntry.QueueId, ex.Message);
                    await SetWarningMessageAsync($"Failed to play video: {ex.Message}");
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        IsPlaying = false;
                        IsVideoPaused = true;
                        NotifyAllProperties();
                        Log.Information("[DJSCREEN] UI reset after playback failure for QueueId={QueueId}", targetEntry.QueueId);
                    });
                    _isInitialPlayback = false;
                    return;
                }
                finally
                {
                    _isInitialPlayback = false;
                }

                if (TryParseSongDuration(targetEntry.VideoLength, out var duration))
                {
                    await UpdateSongDurationAsync(duration, "Queue metadata");
                }
                else
                {
                    Log.Warning("[DJSCREEN] Failed to parse VideoLength for QueueId={QueueId}, VideoLength={VideoLength}", targetEntry.QueueId, targetEntry.VideoLength);
                }

                try
                {
                    await _apiService.PlayAsync(_currentEventId!, targetEntry.QueueId.ToString());
                    Log.Information("[DJSCREEN] Play request sent for event {EventId}, queue {QueueId}: {SongTitle}", _currentEventId, targetEntry.QueueId, targetEntry.SongTitle);
                }
                catch (Exception ex)
                {
                    Log.Error("[DJSCREEN] Failed to send play request for QueueId={QueueId}: {Message}", targetEntry.QueueId, ex.Message);
                    await SetWarningMessageAsync($"Failed to notify server: {ex.Message}");
                }

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    QueueEntries.Remove(targetEntry);
                    for (int i = 0; i < QueueEntries.Count; i++)
                    {
                        QueueEntries[i].Position = i + 1;
                    }
                    OnPropertyChanged(nameof(QueueEntries));
                    Log.Information("[DJSCREEN] Removed played song from queue: QueueId={QueueId}, SongTitle={SongTitle}", targetEntry.QueueId, targetEntry.SongTitle);
                });

                if (_countdownTimer == null)
                {
                    _countdownTimer = new Timer(2000);
                    _countdownTimer.Elapsed += CountdownTimer_Elapsed;
                    _countdownTimer.Start();
                }
                if (_updateTimer == null)
                {
                    _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                    _updateTimer.Tick += UpdateTimer_Tick;
                    _updateTimer.Start();
                    Log.Information("[DJSCREEN] Started update timer for QueueId={QueueId}", targetEntry.QueueId);
                }

                if (QueueEntries.Count <= 20)
                {
                    await UpdateQueueColorsAndRules();
                }
                else
                {
                    Log.Information("[DJSCREEN] Deferred UpdateQueueColorsAndRules due to large queue size: {Count}", QueueEntries.Count);
                    var queueUpdateTask = UpdateQueueColorsAndRules(); // Assign to suppress CS4014
                }
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to play queue: {Message}", ex.Message);
                await SetWarningMessageAsync($"Failed to play: {ex.Message}");
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    IsPlaying = false;
                    IsVideoPaused = true;
                    NotifyAllProperties();
                    Log.Information("[DJSCREEN] UI reset after playback failure");
                });
                _isInitialPlayback = false;
            }
        }

        public async Task HandleSongEnded()
        {
            Log.Information("[DJSCREEN] Handling song ended");
            if (_isDisposing) return;
            try
            {
                if (_updateTimer != null)
                {
                    _updateTimer.Stop();
                    Log.Information("[DJSCREEN] Stopped update timer on song end");
                }

                if (PlayingQueueEntry != null && !string.IsNullOrEmpty(_currentEventId))
                {
                    await _apiService.CompleteSongAsync(_currentEventId, PlayingQueueEntry.QueueId);
                    Log.Information("[DJSCREEN] Completed song for event {EventId}, queue {QueueId}: {SongTitle}", _currentEventId, PlayingQueueEntry.QueueId, PlayingQueueEntry.SongTitle);
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        QueueEntries.Remove(PlayingQueueEntry);
                        for (int i = 0; i < QueueEntries.Count; i++)
                        {
                            QueueEntries[i].Position = i + 1;
                            QueueEntries[i].IsUpNext = i == 0;
                        }
                        OnPropertyChanged(nameof(QueueEntries));
                    });

                    var newOrder = QueueEntries
                        .Select((q, i) => new QueuePosition { QueueId = q.QueueId, Position = i + 1 })
                        .ToList();
                    try
                    {
                        await _apiService.ReorderQueueAsync(_currentEventId!, newOrder);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("[DJSCREEN] Failed to reorder queue after song end: {Message}", ex.Message);
                    }
                }

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ResetPlaybackState();
                    Log.Information("[DJSCREEN] UI updated after song ended");
                });
                TotalSongsPlayed++;
                SungCount++;
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    OnPropertyChanged(nameof(TotalSongsPlayed));
                    OnPropertyChanged(nameof(SungCount));
                });
                Log.Information("[DJSCREEN] Incremented TotalSongsPlayed: {Count}, SungCount: {SungCount}", TotalSongsPlayed, SungCount);

                if (_videoPlayerWindow != null)
                {
                    _videoPlayerWindow.StopVideo();
                }

                if (IsAutoPlayEnabled && !string.IsNullOrEmpty(_currentEventId))
                {
                    await PlayNextAutoPlaySong();
                }
                else
                {
                    Log.Information("[DJSCREEN] AutoPlay is disabled or no event joined, IsAutoPlayEnabled={State}", IsAutoPlayEnabled);
                    await LoadQueueData();
                    if (QueueEntries.Count <= 20)
                    {
                        await UpdateQueueColorsAndRules();
                    }
                    else
                    {
                        Log.Information("[DJSCREEN] Deferred UpdateQueueColorsAndRules due to large queue size: {Count}", QueueEntries.Count);
                        var queueUpdateTask = UpdateQueueColorsAndRules(); // Assign to suppress CS4014
                    }
                    await LoadSungCountAsync();
                }
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to handle song ended: {Message}", ex.Message);
                await SetWarningMessageAsync($"Failed to handle song end: {ex.Message}");
            }
        }

        private async void VideoPlayerWindow_SongEnded(object? sender, EventArgs e)
        {
            Log.Information("[DJSCREEN] SongEnded event received");
            if (_isDisposing) return;
            try
            {
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    await HandleSongEnded();
                });
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to process SongEnded event: {Message}, StackTrace={StackTrace}", ex.Message, ex.StackTrace);
            }
        }

        private async void VideoPlayerWindow_Closed(object? sender, EventArgs e)
        {
            Log.Information("[DJSCREEN] VideoPlayerWindow closed");
            if (_isDisposing) return;
            try
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    CleanupVideoPlayerWindow();
                    DeactivateOverlayBindings();
                    if (_countdownTimer != null)
                    {
                        _countdownTimer.Elapsed -= CountdownTimer_Elapsed;
                        _countdownTimer.Stop();
                        _countdownTimer.Dispose();
                        _countdownTimer = null;
                        Log.Information("[DJSCREEN] Stopped countdown timer due to VideoPlayerWindow close");
                    }
                    CurrentShowState = ShowState.Ended;
                    SetPreShowButton();
                    ResetPlaybackState();
                    CurrentShowState = ShowState.PreShow;
                    Log.Information("[DJSCREEN] Show state reset due to VideoPlayerWindow close");
                });
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to process VideoPlayerWindow close: {Message}", ex.Message);
            }
        }

        public async Task PlayNextAutoPlaySong()
        {
            Log.Information("[DJSCREEN] PlayNextAutoPlaySong invoked");
            if (_isDisposing) return;
            try
            {
                await LoadQueueData();
                QueueEntry? nextEntry;
                lock (_queueLock)
                {
                    Log.Information("[DJSCREEN] Selecting next entry for auto-play");
                    foreach (var entry in QueueEntries)
                    {
                        Log.Information("[DJSCREEN] Queue Entry: QueueId={QueueId}, SongTitle={SongTitle}, IsUpNext={IsUpNext}, IsActive={IsActive}, IsOnHold={IsOnHold}, IsVideoCached={IsVideoCached}, IsSingerLoggedIn={IsSingerLoggedIn}, IsSingerJoined={IsSingerJoined}, IsSingerOnBreak={IsSingerOnBreak}",
                            entry.QueueId, entry.SongTitle, entry.IsUpNext, entry.IsActive, entry.IsOnHold, entry.IsVideoCached, entry.IsSingerLoggedIn, entry.IsSingerJoined, entry.IsSingerOnBreak);
                    }
                    nextEntry = QueueEntries.FirstOrDefault(q => q.IsUpNext && q.IsActive && !q.IsOnHold && q.IsVideoCached && q.IsSingerLoggedIn && q.IsSingerJoined && !q.IsSingerOnBreak) ??
                                QueueEntries.FirstOrDefault(q => q.IsActive && !q.IsOnHold && q.IsVideoCached && q.IsSingerLoggedIn && q.IsSingerJoined && !q.IsSingerOnBreak);
                }

                if (nextEntry != null)
                {
                    Log.Information("[DJSCREEN] Found next entry for auto-play: QueueId={QueueId}, SongTitle={SongTitle}", nextEntry.QueueId, nextEntry.SongTitle);
                    await PlayQueueEntryInternalAsync(nextEntry, requireConfirmation: false);
                }
                else
                {
                    Log.Information("[DJSCREEN] No valid next entry for auto-play");
                    await SetWarningMessageAsync("No valid green singers available for auto-play.");
                    await LoadQueueData();
                    if (QueueEntries.Count <= 20)
                    {
                        await UpdateQueueColorsAndRules();
                    }
                    else
                    {
                        Log.Information("[DJSCREEN] Deferred UpdateQueueColorsAndRules due to large queue size: {Count}", QueueEntries.Count);
                        var queueUpdateTask = UpdateQueueColorsAndRules(); // Assign to suppress CS4014
                    }
                    await LoadSungCountAsync();
                }
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to play next auto-play song: {Message}", ex.Message);
                await SetWarningMessageAsync($"Failed to play next song: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _isDisposing = true;
            try
            {
                if (_warningTimer != null)
                {
                    _warningTimer.Stop();
                    _warningTimer.Dispose();
                    _warningTimer = null;
                }
                if (_countdownTimer != null)
                {
                    _countdownTimer.Elapsed -= CountdownTimer_Elapsed;
                    _countdownTimer.Stop();
                    _countdownTimer.Dispose();
                    _countdownTimer = null;
                }
                if (_debounceTimer != null)
                {
                    _debounceTimer.Elapsed -= DebounceTimer_Elapsed;
                    _debounceTimer.Stop();
                    _debounceTimer.Dispose();
                    _debounceTimer = null;
                }
                if (_updateTimer != null)
                {
                    _updateTimer.Tick -= UpdateTimer_Tick;
                    _updateTimer.Stop();
                    _updateTimer = null;
                    Log.Information("[DJSCREEN] Stopped update timer in Dispose");
                }
                TeardownShowVisuals();
                SetPreShowButton();
                CurrentShowState = ShowState.PreShow;
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to dispose resources: {Message}", ex.Message);
            }
        }
    }
}