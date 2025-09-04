using BNKaraoke.DJ.Models;
using BNKaraoke.DJ.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using Serilog;
using System;
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

        partial void OnBassBoostChanged(int value)
        {
            _videoPlayerWindow?.SetBassGain(value);
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
                        TimeRemaining = "0:00";
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
                                _videoPlayerWindow.MediaPlayer.Volume = (int)_baseVolume;
                                _introMuteSeconds = null;
                            }
                            if (_fadeStartTimeSeconds.HasValue && exactPosition >= _fadeStartTimeSeconds.Value)
                            {
                                var progress = (exactPosition - _fadeStartTimeSeconds.Value) / 7.0;
                                var newVol = _baseVolume * Math.Max(0, 1 - progress);
                                _videoPlayerWindow.MediaPlayer.Volume = (int)newVol;
                                if (exactPosition >= _fadeStartTimeSeconds.Value + 8)
                                {
                                    _fadeStartTimeSeconds = null;
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
                CurrentVideoPosition = "--:--";
                TimeRemainingSeconds = 0;
                TimeRemaining = "0:00";
                StopRestartButtonColor = "#22d3ee";
                PlayingQueueEntry = null;
                SongDuration = TimeSpan.Zero;
                _totalDuration = null;
                _fadeStartTimeSeconds = null;
                _introMuteSeconds = null;
                _baseVolume = 100;
                if (_updateTimer != null)
                {
                    _updateTimer.Stop();
                    _updateTimer = null;
                    Log.Information("[DJSCREEN] Stopped update timer in ResetPlaybackState");
                }
                NotifyAllProperties();
                Log.Information("[DJSCREEN] Playback state reset");
            });
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
                    IsShowActive = false;
                    ShowButtonText = "Start Show";
                    ShowButtonColor = "#22d3ee";
                    if (_videoPlayerWindow != null)
                    {
                        _videoPlayerWindow.EndShow();
                        _videoPlayerWindow = null;
                        Log.Information("[DJSCREEN] VideoPlayerWindow closed");
                    }
                    if (IsPlaying || IsVideoPaused)
                    {
                        ResetPlaybackState();
                        Log.Information("[DJSCREEN] UI updated after show ended");
                    }
                    if (_updateTimer != null)
                    {
                        _updateTimer.Stop();
                        _updateTimer = null;
                        Log.Information("[DJSCREEN] Stopped update timer on show end");
                    }
                    if (_countdownTimer != null)
                    {
                        _countdownTimer.Stop();
                        _countdownTimer.Dispose();
                        _countdownTimer = null;
                        Log.Information("[DJSCREEN] Stopped countdown timer on show end");
                    }
                }
                else
                {
                    if (string.IsNullOrEmpty(_currentEventId))
                    {
                        Log.Information("[DJSCREEN] ToggleShow failed: No event joined");
                        await SetWarningMessageAsync("Please join an event before starting the show.");
                        return;
                    }
                    Log.Information("[DJSCREEN] Starting show");
                    _videoPlayerWindow = new VideoPlayerWindow();
                    if (_videoPlayerWindow.MediaPlayer == null)
                    {
                        Log.Error("[DJSCREEN] Failed to initialize VideoPlayerWindow: MediaPlayer is null");
                        await SetWarningMessageAsync("Failed to start show: Video player initialization failed. Check LibVLC setup.");
                        _videoPlayerWindow.Close();
                        _videoPlayerWindow = null;
                        return;
                    }
                    _videoPlayerWindow.SetBassGain(BassBoost);
                    _videoPlayerWindow.SongEnded += VideoPlayerWindow_SongEnded;
                    _videoPlayerWindow.Closed += VideoPlayerWindow_Closed;
                    _videoPlayerWindow.MediaPlayer.PositionChanged += OnVLCPositionChanged;
                    _videoPlayerWindow.MediaPlayer.EncounteredError += OnVLCError;
                    _videoPlayerWindow.Show();
                    IsShowActive = true;
                    ShowButtonText = "End Show";
                    ShowButtonColor = "#FF0000";
                    NotifyAllProperties();
                    Log.Information("[DJSCREEN] Show started, VideoPlayerWindow shown with idle title");
                }
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to toggle show: {Message}", ex.Message);
                await SetWarningMessageAsync($"Failed to toggle show: {ex.Message}");
                IsShowActive = false;
                ShowButtonText = "Start Show";
                ShowButtonColor = "#22d3ee";
                if (_videoPlayerWindow != null)
                {
                    _videoPlayerWindow.EndShow();
                    _videoPlayerWindow = null;
                }
                ResetPlaybackState();
            }
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

            if (_videoPlayerWindow == null || _videoPlayerWindow.MediaPlayer == null)
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

                    if (SelectedQueueEntry != null && (!SelectedQueueEntry.IsSingerJoined || SelectedQueueEntry.IsSingerOnBreak || !SelectedQueueEntry.IsActive || SelectedQueueEntry.IsOnHold || !SelectedQueueEntry.IsVideoCached))
                    {
                        Log.Information("[DJSCREEN] Invalid SelectedQueueEntry for auto-play, resetting to null: QueueId={QueueId}, SongTitle={SongTitle}", SelectedQueueEntry.QueueId, SelectedQueueEntry.SongTitle);
                        SelectedQueueEntry = null;
                        OnPropertyChanged(nameof(SelectedQueueEntry));
                    }

                    targetEntry = QueueEntries.FirstOrDefault(q => q.IsUpNext && q.IsActive && !q.IsOnHold && q.IsVideoCached && q.IsSingerLoggedIn && q.IsSingerJoined && !q.IsSingerOnBreak) ??
                                  QueueEntries.FirstOrDefault(q => q.IsActive && !q.IsOnHold && q.IsVideoCached && q.IsSingerLoggedIn && q.IsSingerJoined && !q.IsSingerOnBreak);

                    if (targetEntry == null && SelectedQueueEntry != null)
                    {
                        Log.Information("[DJSCREEN] Falling back to SelectedQueueEntry: QueueId={QueueId}, SongTitle={SongTitle}", SelectedQueueEntry.QueueId, SelectedQueueEntry.SongTitle);
                        targetEntry = SelectedQueueEntry;
                    }
                }

                if (targetEntry == null)
                {
                    Log.Information("[DJSCREEN] Play failed: No valid green singer available. SelectedQueueEntry={Selected}, UpNextCount={UpNextCount}, GreenSingerCount={GreenCount}",
                        SelectedQueueEntry?.QueueId ?? -1, QueueEntries.Count(q => q.IsUpNext), QueueEntries.Count(q => q.IsActive && !q.IsOnHold && q.IsVideoCached && q.IsSingerLoggedIn && q.IsSingerJoined && !q.IsSingerOnBreak));
                    await SetWarningMessageAsync("No valid green singers available to play.");
                    return;
                }

                Log.Information("[DJSCREEN] Selected target entry for play: QueueId={QueueId}, SongTitle={SongTitle}, IsUpNext={IsUpNext}, IsActive={IsActive}, IsOnHold={IsOnHold}, IsVideoCached={IsVideoCached}, IsSingerLoggedIn={IsSingerLoggedIn}, IsSingerJoined={IsSingerJoined}, IsSingerOnBreak={IsSingerOnBreak}",
                    targetEntry.QueueId, targetEntry.SongTitle, targetEntry.IsUpNext, targetEntry.IsActive, targetEntry.IsOnHold, targetEntry.IsVideoCached, targetEntry.IsSingerLoggedIn, targetEntry.IsSingerJoined, targetEntry.IsSingerOnBreak);

                if (_videoPlayerWindow == null || _videoPlayerWindow.MediaPlayer == null)
                {
                    Log.Information("[DJSCREEN] Initializing new VideoPlayerWindow for QueueId={QueueId}", targetEntry.QueueId);
                    _videoPlayerWindow?.Close();
                    _videoPlayerWindow = new VideoPlayerWindow();
                    if (_videoPlayerWindow.MediaPlayer == null)
                    {
                        Log.Error("[DJSCREEN] Failed to initialize VideoPlayerWindow for QueueId={QueueId}: MediaPlayer is null", targetEntry.QueueId);
                        await SetWarningMessageAsync("Failed to play: Video player initialization failed. Check LibVLC setup.");
                        _videoPlayerWindow.Close();
                        _videoPlayerWindow = null;
                        return;
                    }
                    _videoPlayerWindow.SetBassGain(BassBoost);
                    _videoPlayerWindow.SongEnded += VideoPlayerWindow_SongEnded;
                    _videoPlayerWindow.Closed += VideoPlayerWindow_Closed;
                    _videoPlayerWindow.MediaPlayer.PositionChanged += OnVLCPositionChanged;
                    _videoPlayerWindow.MediaPlayer.EncounteredError += OnVLCError;
                    Log.Information("[DJSCREEN] Created new VideoPlayerWindow for QueueId={QueueId}", targetEntry.QueueId);
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
                    SongPosition = 0;
                    _lastPosition = 0;
                    CurrentVideoPosition = "0:00";
                    TimeRemainingSeconds = 0;
                    TimeRemaining = "0:00";
                    StopRestartButtonColor = "#22d3ee";
                    NotifyAllProperties();
                    Log.Information("[DJSCREEN] UI updated for new song: QueueId={QueueId}, SongTitle={SongTitle}", targetEntry.QueueId, targetEntry.SongTitle);
                });

                _isInitialPlayback = true;
                try
                {
                    Log.Information("[DJSCREEN] Attempting to play video for QueueId={QueueId}, Path={Path}", targetEntry.QueueId, videoPath);
                    _videoPlayerWindow.PlayVideo(videoPath);
                    _videoPlayerWindow.Show();
                    _baseVolume = 100;
                    if (targetEntry.NormalizationGain.HasValue)
                    {
                        _baseVolume = 100 * Math.Pow(10, targetEntry.NormalizationGain.Value / 20.0);
                    }
                    _fadeStartTimeSeconds = (targetEntry.FadeStartTime.HasValue && targetEntry.FadeStartTime.Value > 0)
                        ? targetEntry.FadeStartTime.Value
                        : null;
                    _introMuteSeconds = (targetEntry.IntroMuteDuration.HasValue && targetEntry.IntroMuteDuration.Value > 0)
                        ? targetEntry.IntroMuteDuration.Value
                        : null;
                    if (_videoPlayerWindow.MediaPlayer != null)
                    {
                        _videoPlayerWindow.MediaPlayer.Volume = _introMuteSeconds.HasValue ? 0 : (int)_baseVolume;
                    }
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

                if (TimeSpan.TryParseExact(targetEntry.VideoLength, @"m\:ss", null, out var duration))
                {
                    var effectiveSeconds = duration.TotalSeconds;
                    if (targetEntry.FadeStartTime.HasValue && targetEntry.FadeStartTime.Value > 0 &&
                        targetEntry.FadeStartTime.Value + 8 < effectiveSeconds)
                    {
                        effectiveSeconds = targetEntry.FadeStartTime.Value + 8;
                    }
                    _totalDuration = TimeSpan.FromSeconds(effectiveSeconds);
                    SongDuration = duration;
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        TimeRemainingSeconds = (int)effectiveSeconds;
                        TimeRemaining = _totalDuration.Value.ToString(@"m\:ss");
                        OnPropertyChanged(nameof(SongDuration));
                        NotifyAllProperties();
                        Log.Information("[DJSCREEN] Set durations: Full={Full}, Effective={Effective}", SongDuration, _totalDuration);
                    });
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

        [RelayCommand]
        public async Task PlayQueueEntryAsync(QueueEntry? entry)
        {
            await PlayQueueEntryInternalAsync(entry, true);
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
                        MessageBox.Show(confirmMessage, "Confirm Song Playback", MessageBoxButton.YesNo, MessageBoxImage.Question));
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

                if (_videoPlayerWindow == null || _videoPlayerWindow.MediaPlayer == null)
                {
                    Log.Information("[DJSCREEN] Initializing new VideoPlayerWindow for QueueId={QueueId}", targetEntry.QueueId);
                    _videoPlayerWindow?.Close();
                    _videoPlayerWindow = new VideoPlayerWindow();
                    if (_videoPlayerWindow.MediaPlayer == null)
                    {
                        Log.Error("[DJSCREEN] Failed to initialize VideoPlayerWindow for QueueId={QueueId}: MediaPlayer is null", targetEntry.QueueId);
                        await SetWarningMessageAsync("Failed to play: Video player initialization failed. Check LibVLC setup.");
                        _videoPlayerWindow.Close();
                        _videoPlayerWindow = null;
                        return;
                    }
                    _videoPlayerWindow.SetBassGain(BassBoost);
                    _videoPlayerWindow.SongEnded += VideoPlayerWindow_SongEnded;
                    _videoPlayerWindow.Closed += VideoPlayerWindow_Closed;
                    _videoPlayerWindow.MediaPlayer.PositionChanged += OnVLCPositionChanged;
                    _videoPlayerWindow.MediaPlayer.EncounteredError += OnVLCError;
                    Log.Information("[DJSCREEN] Created new VideoPlayerWindow for QueueId={QueueId}", targetEntry.QueueId);
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
                    SongPosition = 0;
                    _lastPosition = 0;
                    CurrentVideoPosition = "0:00";
                    TimeRemainingSeconds = 0;
                    TimeRemaining = "0:00";
                    StopRestartButtonColor = "#22d3ee";
                    NotifyAllProperties();
                    Log.Information("[DJSCREEN] UI updated for new song: QueueId={QueueId}, SongTitle={SongTitle}", targetEntry.QueueId, targetEntry.SongTitle);
                });

                _isInitialPlayback = true;
                try
                {
                    Log.Information("[DJSCREEN] Attempting to play video for QueueId={QueueId}, Path={Path}", targetEntry.QueueId, videoPath);
                    _videoPlayerWindow.PlayVideo(videoPath);
                    _videoPlayerWindow.Show();
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

                if (TimeSpan.TryParseExact(targetEntry.VideoLength, @"m\:ss", null, out var duration))
                {
                    var effectiveSeconds = duration.TotalSeconds;
                    if (targetEntry.FadeStartTime.HasValue && targetEntry.FadeStartTime.Value > 0 &&
                        targetEntry.FadeStartTime.Value + 8 < effectiveSeconds)
                    {
                        effectiveSeconds = targetEntry.FadeStartTime.Value + 8;
                    }
                    _totalDuration = TimeSpan.FromSeconds(effectiveSeconds);
                    SongDuration = duration;
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        TimeRemainingSeconds = (int)effectiveSeconds;
                        TimeRemaining = _totalDuration.Value.ToString(@"m\:ss");
                        OnPropertyChanged(nameof(SongDuration));
                        NotifyAllProperties();
                        Log.Information("[DJSCREEN] Set durations: Full={Full}, Effective={Effective}", SongDuration, _totalDuration);
                    });
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
                    if (_videoPlayerWindow != null)
                    {
                        _videoPlayerWindow.SongEnded -= VideoPlayerWindow_SongEnded;
                        _videoPlayerWindow.Closed -= VideoPlayerWindow_Closed;
                        if (_videoPlayerWindow.MediaPlayer != null)
                        {
                            _videoPlayerWindow.MediaPlayer.PositionChanged -= OnVLCPositionChanged;
                            _videoPlayerWindow.MediaPlayer.EncounteredError -= OnVLCError;
                        }
                        _videoPlayerWindow = null;
                    }
                    IsShowActive = false;
                    ShowButtonText = "Start Show";
                    ShowButtonColor = "#22d3ee";
                    ResetPlaybackState();
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
                    _countdownTimer.Stop();
                    _countdownTimer.Dispose();
                    _countdownTimer = null;
                }
                if (_debounceTimer != null)
                {
                    _debounceTimer.Stop();
                    _debounceTimer.Dispose();
                    _debounceTimer = null;
                }
                if (_updateTimer != null)
                {
                    _updateTimer.Stop();
                    _updateTimer = null;
                    Log.Information("[DJSCREEN] Stopped update timer in Dispose");
                }
                if (_videoPlayerWindow != null)
                {
                    if (_videoPlayerWindow.MediaPlayer != null)
                    {
                        _videoPlayerWindow.MediaPlayer.PositionChanged -= OnVLCPositionChanged;
                        _videoPlayerWindow.MediaPlayer.EncounteredError -= OnVLCError;
                    }
                    _videoPlayerWindow.SongEnded -= VideoPlayerWindow_SongEnded;
                    _videoPlayerWindow.Closed -= VideoPlayerWindow_Closed;
                    _videoPlayerWindow.EndShow();
                    _videoPlayerWindow = null;
                }
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to dispose resources: {Message}", ex.Message);
            }
        }
    }
}