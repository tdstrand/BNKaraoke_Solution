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

namespace BNKaraoke.DJ.ViewModels
{
    public partial class DJScreenViewModel
    {
        private Timer? _warningTimer;
        private Timer? _countdownTimer;
        private DispatcherTimer? _updateTimer;
        private bool _isDisposing;
        private TimeSpan? _totalDuration;
        private bool _countdownStarted;
        private bool _isSeeking;
        private bool _isInitialPlayback;
        private bool _wasPlaying;
        private readonly object _queueLock = new();
        private double _lastSeekPosition = -1;
        private bool _isUserSeeking;

        [ObservableProperty]
        private double _sliderPosition;

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

        private void WarningTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            if (_isDisposing) return;
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
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
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if ((IsPlaying || IsVideoPaused) && _totalDuration.HasValue && _videoPlayerWindow?.MediaPlayer != null)
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
                        }
                    }
                    else
                    {
                        TimeRemainingSeconds = 0;
                        TimeRemaining = "0:00";
                        CurrentVideoPosition = "--:--";
                        SliderPosition = 0;
                        OnPropertyChanged(nameof(SliderPosition));
                        OnPropertyChanged(nameof(CurrentVideoPosition));
                        OnPropertyChanged(nameof(TimeRemaining));
                        OnPropertyChanged(nameof(TimeRemainingSeconds));
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
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_videoPlayerWindow.MediaPlayer != null)
                    {
                        var currentTime = TimeSpan.FromMilliseconds(_videoPlayerWindow.MediaPlayer.Time);
                        CurrentVideoPosition = currentTime.ToString(@"m\:ss");
                        if (!_isUserSeeking)
                        {
                            var newPosition = currentTime.TotalSeconds;
                            if (Math.Abs(newPosition - SliderPosition) > 0.1)
                            {
                                SliderPosition = newPosition;
                                Log.Verbose("[DJSCREEN] Updated SliderPosition to {Position}", newPosition);
                                OnPropertyChanged(nameof(SliderPosition));
                            }
                        }
                        OnPropertyChanged(nameof(CurrentVideoPosition));
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
            if (_isDisposing || _videoPlayerWindow?.MediaPlayer == null) return;
            try
            {
                _isSeeking = true;
                _isUserSeeking = true;
                _wasPlaying = _videoPlayerWindow.MediaPlayer.IsPlaying || IsPlaying;
                if (_videoPlayerWindow.MediaPlayer.IsPlaying)
                {
                    _videoPlayerWindow.MediaPlayer.Pause();
                    Log.Information("[DJSCREEN] Paused video for seeking");
                }
                Log.Information("[DJSCREEN] Started seeking, WasPlaying={WasPlaying}, MediaState={State}", _wasPlaying, _videoPlayerWindow.MediaPlayer.State);
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to start seeking: {Message}", ex.Message);
            }
        }

        [RelayCommand]
        private void StopSeeking()
        {
            if (_isDisposing || _videoPlayerWindow?.MediaPlayer == null) return;
            try
            {
                _isSeeking = false;
                _isUserSeeking = false;
                if (_wasPlaying && _videoPlayerWindow.MediaPlayer.State != VLCState.Playing)
                {
                    _videoPlayerWindow.MediaPlayer.Play();
                    Log.Information("[DJSCREEN] Resumed video after seeking, MediaState={State}", _videoPlayerWindow.MediaPlayer.State);
                }
                Log.Information("[DJSCREEN] Stopped seeking");
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to stop seeking: {Message}", ex.Message);
            }
        }

        [RelayCommand]
        private async Task SeekSong(double position)
        {
            if (_isDisposing || _videoPlayerWindow?.MediaPlayer == null || _isInitialPlayback || _videoPlayerWindow.MediaPlayer.State == VLCState.Stopped)
            {
                Log.Verbose("[DJSCREEN] SeekSong skipped: Disposing={Disposing}, MediaPlayer={MediaPlayer}, InitialPlayback={InitialPlayback}, State={State}",
                    _isDisposing, _videoPlayerWindow?.MediaPlayer != null, _isInitialPlayback, _videoPlayerWindow?.MediaPlayer?.State);
                return;
            }
            try
            {
                Log.Information("[DJSCREEN] SeekSong invoked with position: {Position}, IsSeeking={IsSeeking}, MediaState={State}",
                    position, _isSeeking, _videoPlayerWindow.MediaPlayer.State);
                var currentTime = _videoPlayerWindow.MediaPlayer.Time / 1000.0;
                if (!_isSeeking && Math.Abs(position - currentTime) < 2.0)
                {
                    Log.Verbose("[DJSCREEN] SeekSong skipped: Position={Position} too close to current time={CurrentTime}", position, currentTime);
                    return;
                }
                if (Math.Abs(position - _lastSeekPosition) < 0.1)
                {
                    Log.Verbose("[DJSCREEN] SeekSong skipped: Position={Position} too close to last seek={LastSeekPosition}", position, _lastSeekPosition);
                    return;
                }
                _isSeeking = true;
                _videoPlayerWindow.MediaPlayer.Pause();
                SliderPosition = position;
                CurrentVideoPosition = TimeSpan.FromSeconds(position).ToString(@"m\:ss");
                OnPropertyChanged(nameof(SliderPosition));
                OnPropertyChanged(nameof(CurrentVideoPosition));
                _videoPlayerWindow.MediaPlayer.Time = (long)(position * 1000);
                _lastSeekPosition = position;
                await Task.Delay(100);
                if (_wasPlaying && _videoPlayerWindow.MediaPlayer.State != VLCState.Playing)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        Log.Information("[DJSCREEN] Attempting to resume playback after seek, Attempt={Attempt}, MediaState={State}", i + 1, _videoPlayerWindow.MediaPlayer.State);
                        _videoPlayerWindow.MediaPlayer.Play();
                        await Task.Delay(100);
                        if (_videoPlayerWindow.MediaPlayer.State == VLCState.Playing)
                        {
                            Log.Information("[DJSCREEN] Resumed playback after seek to position: {Position}, Attempt={Attempt}", position, i + 1);
                            break;
                        }
                    }
                    if (_videoPlayerWindow.MediaPlayer.State != VLCState.Playing)
                    {
                        Log.Warning("[DJSCREEN] Failed to resume playback after seek to position: {Position}, MediaState={State}", position, _videoPlayerWindow.MediaPlayer.State);
                        SetWarningMessage("Failed to resume playback after seeking. Please try pausing and playing again.");
                    }
                }
                _isSeeking = false;
                Log.Information("[DJSCREEN] Seeked to position: {Position}, MediaState={State}", position, _videoPlayerWindow.MediaPlayer.State);
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to seek song: {Message}", ex.Message);
                SetWarningMessage($"Failed to seek song: {ex.Message}");
            }
        }

        private void NotifyAllProperties()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                OnPropertyChanged(nameof(PlayingQueueEntry));
                OnPropertyChanged(nameof(SelectedQueueEntry));
                OnPropertyChanged(nameof(IsPlaying));
                OnPropertyChanged(nameof(IsVideoPaused));
                OnPropertyChanged(nameof(SliderPosition));
                OnPropertyChanged(nameof(CurrentVideoPosition));
                OnPropertyChanged(nameof(TimeRemaining));
                OnPropertyChanged(nameof(TimeRemainingSeconds));
                OnPropertyChanged(nameof(StopRestartButtonColor));
                OnPropertyChanged(nameof(SongDuration));
                CommandManager.InvalidateRequerySuggested();
                Log.Information("[DJSCREEN] Notified all UI properties");
                Dispatcher.Yield(DispatcherPriority.Render);
            });
        }

        [RelayCommand]
        private async Task PlayAsync()
        {
            Log.Information("[DJSCREEN] Play/Pause command invoked");
            if (_isDisposing) return;
            if (!IsShowActive)
            {
                Log.Information("[DJSCREEN] Play failed: Show not started");
                SetWarningMessage("Please start the show first.");
                return;
            }

            if (QueueEntries.Count == 0) // CS8604: QueueEntries is never null
            {
                Log.Information("[DJSCREEN] Play failed: Queue is empty");
                SetWarningMessage("No songs in the queue.");
                return;
            }

            if (IsVideoPaused && PlayingQueueEntry != null && _videoPlayerWindow?.MediaPlayer != null)
            {
                try
                {
                    _videoPlayerWindow.MediaPlayer.Play();
                    Application.Current.Dispatcher.Invoke(() =>
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
                        await _apiService.PlayAsync(_currentEventId, PlayingQueueEntry.QueueId.ToString()); // CS8602: Checked above
                        Log.Information("[DJSCREEN] Play request sent for event {EventId}, queue {QueueId}: {SongTitle}", _currentEventId, PlayingQueueEntry.QueueId, PlayingQueueEntry.SongTitle);
                    }
                    if (_updateTimer == null)
                    {
                        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
                        _updateTimer.Tick += UpdateTimer_Tick;
                        _updateTimer.Start();
                        Log.Information("[DJSCREEN] Started update timer for resume");
                    }
                    return;
                }
                catch (Exception ex)
                {
                    Log.Error("[DJSCREEN] Failed to resume queue {QueueId}: {Message}", PlayingQueueEntry.QueueId, ex.Message);
                    SetWarningMessage($"Failed to resume: {ex.Message}");
                    return;
                }
            }

            if (IsPlaying && _videoPlayerWindow?.MediaPlayer != null)
            {
                try
                {
                    _videoPlayerWindow.PauseVideo();
                    Application.Current.Dispatcher.Invoke(() =>
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
                    SetWarningMessage($"Failed to pause: {ex.Message}");
                    return;
                }
            }

            if (string.IsNullOrEmpty(_currentEventId))
            {
                Log.Information("[DJSCREEN] Play failed: No event joined");
                SetWarningMessage("Please join an event.");
                return;
            }

            try
            {
                await LoadQueueData();

                QueueEntry? targetEntry;
                lock (_queueLock)
                {
                    Log.Information("[DJSCREEN] Selecting target entry for auto-play");
                    Log.Information("[DJSCREEN] SelectedQueueEntry: QueueId={QueueId}, SongTitle={SongTitle}, IsUpNext={IsUpNext}, IsSingerJoined={IsSingerJoined}, IsSingerOnBreak={IsSingerOnBreak}",
                        SelectedQueueEntry?.QueueId ?? -1, SelectedQueueEntry?.SongTitle ?? "null", SelectedQueueEntry?.IsUpNext ?? false, SelectedQueueEntry?.IsSingerJoined ?? false, SelectedQueueEntry?.IsSingerOnBreak ?? false);

                    if (SelectedQueueEntry != null && (!SelectedQueueEntry.IsSingerJoined || SelectedQueueEntry.IsSingerOnBreak || !SelectedQueueEntry.IsActive || SelectedQueueEntry.IsOnHold || !SelectedQueueEntry.IsVideoCached))
                    {
                        Log.Information("[DJSCREEN] Invalid SelectedQueueEntry for auto-play, resetting to null: QueueId={QueueId}, SongTitle={SongTitle}", SelectedQueueEntry.QueueId, SelectedQueueEntry.SongTitle);
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            SelectedQueueEntry = null;
                            OnPropertyChanged(nameof(SelectedQueueEntry));
                        });
                    }

                    targetEntry = SelectedQueueEntry ?? QueueEntries.FirstOrDefault(q => q.IsUpNext && q.IsActive && !q.IsOnHold && q.IsVideoCached && q.IsSingerLoggedIn && q.IsSingerJoined && !q.IsSingerOnBreak) ??
                                  QueueEntries.FirstOrDefault(q => q.IsActive && !q.IsOnHold && q.IsVideoCached && q.IsSingerLoggedIn && q.IsSingerJoined && !q.IsSingerOnBreak); // CS8604: QueueEntries is never null

                    if (SelectedQueueEntry != null && targetEntry != SelectedQueueEntry)
                    {
                        Log.Information("[DJSCREEN] SelectedQueueEntry rejected: QueueId={QueueId}, SongTitle={SongTitle}, Reason=Invalid singer status (IsSingerJoined={IsSingerJoined}, IsSingerOnBreak={IsSingerOnBreak})",
                            SelectedQueueEntry.QueueId, SelectedQueueEntry.SongTitle, SelectedQueueEntry.IsSingerJoined, SelectedQueueEntry.IsSingerOnBreak);
                    }
                }

                if (targetEntry == null)
                {
                    Log.Information("[DJSCREEN] Play failed: No valid green singer available. SelectedQueueEntry={Selected}, UpNextCount={UpNextCount}, GreenSingerCount={GreenCount}",
                        SelectedQueueEntry?.QueueId ?? -1, QueueEntries.Count(q => q.IsUpNext), QueueEntries.Count(q => q.IsActive && !q.IsOnHold && q.IsVideoCached && q.IsSingerLoggedIn && q.IsSingerJoined && !q.IsSingerOnBreak));
                    SetWarningMessage("No valid green singers available to play.");
                    return;
                }

                Log.Information("[DJSCREEN] Selected target entry for play: QueueId={QueueId}, SongTitle={SongTitle}, IsUpNext={IsUpNext}, IsActive={IsActive}, IsOnHold={IsOnHold}, IsVideoCached={IsVideoCached}, IsSingerLoggedIn={IsSingerLoggedIn}, IsSingerJoined={IsSingerJoined}, IsSingerOnBreak={IsSingerOnBreak}",
                    targetEntry.QueueId, targetEntry.SongTitle, targetEntry.IsUpNext, targetEntry.IsActive, targetEntry.IsOnHold, targetEntry.IsVideoCached, targetEntry.IsSingerLoggedIn, targetEntry.IsSingerJoined, targetEntry.IsSingerOnBreak);

                if (_videoPlayerWindow == null || _videoPlayerWindow.MediaPlayer == null)
                {
                    _videoPlayerWindow?.Close();
                    _videoPlayerWindow = new VideoPlayerWindow();
                    _videoPlayerWindow.SongEnded += VideoPlayerWindow_SongEnded;
                    _videoPlayerWindow.Closed += VideoPlayerWindow_Closed;
                    Log.Information("[DJSCREEN] Created new VideoPlayerWindow for QueueId={QueueId}", targetEntry.QueueId);
                }

                string videoPath = Path.Combine(_settingsService.Settings.VideoCachePath, $"{targetEntry.SongId}.mp4");
                Log.Information("[DJSCREEN] Video path for QueueId={QueueId}: {Path}", targetEntry.QueueId, videoPath);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    PlayingQueueEntry = targetEntry;
                    SelectedQueueEntry = targetEntry;
                    IsPlaying = true;
                    IsVideoPaused = false;
                    SliderPosition = 0;
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
                    Log.Information("[DJSCREEN] Video playback started for QueueId={QueueId}, Path={Path}", targetEntry.QueueId, videoPath);
                }
                catch (Exception ex)
                {
                    Log.Error("[DJSCREEN] Failed to start video playback for QueueId={QueueId}: {Message}", targetEntry.QueueId, ex.Message);
                    SetWarningMessage($"Failed to play video: {ex.Message}");
                    Application.Current.Dispatcher.Invoke(() =>
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
                    _totalDuration = duration;
                    SongDuration = duration;
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        TimeRemainingSeconds = (int)(duration.TotalSeconds);
                        TimeRemaining = duration.ToString(@"m\:ss");
                        OnPropertyChanged(nameof(SongDuration));
                        NotifyAllProperties();
                        Log.Information("[DJSCREEN] Set total duration: {Duration}", duration);
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
                    SetWarningMessage($"Failed to notify server: {ex.Message}");
                }

                Application.Current.Dispatcher.Invoke(() =>
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
                    _countdownTimer = new Timer(1000);
                    _countdownTimer.Elapsed += CountdownTimer_Elapsed;
                    _countdownTimer.Start();
                }
                if (_updateTimer == null)
                {
                    _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
                    _updateTimer.Tick += UpdateTimer_Tick;
                    _updateTimer.Start();
                    Log.Information("[DJSCREEN] Started update timer for QueueId={QueueId}", targetEntry.QueueId);
                }

                await UpdateQueueColorsAndRules();
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to play queue: {Message}", ex.Message);
                SetWarningMessage($"Failed to play: {ex.Message}");
                Application.Current.Dispatcher.Invoke(() =>
                {
                    IsPlaying = false;
                    IsVideoPaused = true;
                    NotifyAllProperties();
                });
                _isInitialPlayback = false;
            }
        }

        [RelayCommand]
        public async Task PlayQueueEntryAsync(QueueEntry entry)
        {
            if (_isDisposing || entry == null)
            {
                Log.Information("[DJSCREEN] PlayQueueEntryAsync skipped: Disposing={Disposing}, Entry={Entry}", _isDisposing, entry == null);
                return;
            }
            Log.Information("[DJSCREEN] PlayQueueEntryAsync invoked for QueueId={QueueId}, SongTitle={SongTitle}, IsSingerOnBreak={IsSingerOnBreak}", entry.QueueId, entry.SongTitle, entry.IsSingerOnBreak);

            if (!IsShowActive)
            {
                Log.Information("[DJSCREEN] PlayQueueEntry failed: Show not started");
                SetWarningMessage("Please start the show first.");
                return;
            }

            try
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
                    return;
                }

                if ((IsPlaying || IsVideoPaused) && PlayingQueueEntry != null && _videoPlayerWindow != null)
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
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            IsPlaying = false;
                            IsVideoPaused = true;
                            SliderPosition = 0;
                            CurrentVideoPosition = "--:--";
                            TimeRemainingSeconds = 0;
                            TimeRemaining = "0:00";
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
                        SetWarningMessage($"Failed to stop current song: {ex.Message}");
                        return;
                    }
                }

                if (string.IsNullOrEmpty(_currentEventId))
                {
                    Log.Information("[DJSCREEN] PlayQueueEntry failed: No event joined");
                    SetWarningMessage("Please join an event.");
                    return;
                }

                Log.Information("[DJSCREEN] Starting playback setup for QueueId={QueueId}", entry.QueueId);
                QueueEntry targetEntry = entry;
                Log.Information("[DJSCREEN] Target entry selected: QueueId={QueueId}, SongTitle={SongTitle}, IsSingerOnBreak={IsSingerOnBreak}",
                    targetEntry.QueueId, targetEntry.SongTitle, targetEntry.IsSingerOnBreak);

                if (_videoPlayerWindow == null || _videoPlayerWindow.MediaPlayer == null)
                {
                    _videoPlayerWindow?.Close();
                    _videoPlayerWindow = new VideoPlayerWindow();
                    _videoPlayerWindow.SongEnded += VideoPlayerWindow_SongEnded;
                    _videoPlayerWindow.Closed += VideoPlayerWindow_Closed;
                    Log.Information("[DJSCREEN] Created new VideoPlayerWindow for QueueId={QueueId}", targetEntry.QueueId);
                }

                string videoPath = Path.Combine(_settingsService.Settings.VideoCachePath, $"{targetEntry.SongId}.mp4");
                Log.Information("[DJSCREEN] Video path for QueueId={QueueId}: {Path}", targetEntry.QueueId, videoPath);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    PlayingQueueEntry = targetEntry;
                    SelectedQueueEntry = targetEntry;
                    IsPlaying = true;
                    IsVideoPaused = false;
                    SliderPosition = 0;
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
                    Log.Information("[DJSCREEN] Video playback started for QueueId={QueueId}, Path={Path}", targetEntry.QueueId, videoPath);
                }
                catch (Exception ex)
                {
                    Log.Error("[DJSCREEN] Failed to start video playback for QueueId={QueueId}: {Message}", targetEntry.QueueId, ex.Message);
                    SetWarningMessage($"Failed to play video: {ex.Message}");
                    Application.Current.Dispatcher.Invoke(() =>
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
                    _totalDuration = duration;
                    SongDuration = duration;
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        TimeRemainingSeconds = (int)(duration.TotalSeconds);
                        TimeRemaining = duration.ToString(@"m\:ss");
                        OnPropertyChanged(nameof(SongDuration));
                        NotifyAllProperties();
                        Log.Information("[DJSCREEN] Set total duration: {Duration}", duration);
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
                    SetWarningMessage($"Failed to notify server: {ex.Message}");
                }

                Application.Current.Dispatcher.Invoke(() =>
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
                    _countdownTimer = new Timer(1000);
                    _countdownTimer.Elapsed += CountdownTimer_Elapsed;
                    _countdownTimer.Start();
                }
                if (_updateTimer == null)
                {
                    _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
                    _updateTimer.Tick += UpdateTimer_Tick;
                    _updateTimer.Start();
                    Log.Information("[DJSCREEN] Started update timer for QueueId={QueueId}", targetEntry.QueueId);
                }

                await UpdateQueueColorsAndRules();
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to play queue entry {QueueId}: {Message}, StackTrace={StackTrace}", entry.QueueId, ex.Message, ex.StackTrace);
                SetWarningMessage($"Failed to play: {ex.Message}");
                Application.Current.Dispatcher.Invoke(() =>
                {
                    IsPlaying = false;
                    IsVideoPaused = true;
                    NotifyAllProperties();
                    Log.Information("[DJSCREEN] UI reset after failure for QueueId={QueueId}", entry.QueueId);
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
                    QueueEntries.Remove(PlayingQueueEntry);
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        for (int i = 0; i < QueueEntries.Count; i++)
                        {
                            QueueEntries[i].IsUpNext = i == 0;
                        }
                        OnPropertyChanged(nameof(QueueEntries));
                    });
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    IsPlaying = false;
                    IsVideoPaused = false;
                    SliderPosition = 0;
                    CurrentVideoPosition = "--:--";
                    TimeRemainingSeconds = 0;
                    TimeRemaining = "0:00";
                    StopRestartButtonColor = "#22d3ee";
                    PlayingQueueEntry = null;
                    NotifyAllProperties();
                    Log.Information("[DJSCREEN] UI updated after song ended");
                });
                TotalSongsPlayed++;
                SungCount++;
                Application.Current.Dispatcher.Invoke(() =>
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
                    await UpdateQueueColorsAndRules();
                    await LoadSungCountAsync();
                }
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to handle song ended: {Message}", ex.Message);
                SetWarningMessage($"Failed to handle song end: {ex.Message}");
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

        private void VideoPlayerWindow_Closed(object? sender, EventArgs e)
        {
            Log.Information("[DJSCREEN] VideoPlayerWindow closed");
            if (_isDisposing) return;
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_videoPlayerWindow != null)
                    {
                        _videoPlayerWindow.SongEnded -= VideoPlayerWindow_SongEnded;
                        _videoPlayerWindow.Closed -= VideoPlayerWindow_Closed;
                        _videoPlayerWindow = null;
                    }
                    IsShowActive = false;
                    ShowButtonText = "Start Show";
                    ShowButtonColor = "#22d3ee";
                    if (IsPlaying || IsVideoPaused)
                    {
                        IsPlaying = false;
                        IsVideoPaused = false;
                        SliderPosition = 0;
                        CurrentVideoPosition = "--:--";
                        TimeRemainingSeconds = 0;
                        TimeRemaining = "0:00";
                        StopRestartButtonColor = "#22d3ee";
                        PlayingQueueEntry = null;
                        NotifyAllProperties();
                        Log.Information("[DJSCREEN] UI updated after VideoPlayerWindow closed");
                    }
                    Log.Information("[DJSCREEN] Show state reset due to VideoPlayerWindow close");
                });
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to process VideoPlayerWindow close: {Message}", ex.Message);
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
                if (_updateTimer != null)
                {
                    _updateTimer.Stop();
                    _updateTimer = null;
                }
                if (_videoPlayerWindow != null)
                {
                    _videoPlayerWindow.SongEnded -= VideoPlayerWindow_SongEnded;
                    _videoPlayerWindow.Closed -= VideoPlayerWindow_Closed;
                    _videoPlayerWindow.Close();
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