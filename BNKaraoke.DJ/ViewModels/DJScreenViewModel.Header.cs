using BNKaraoke.DJ.Models;
using BNKaraoke.DJ.Services;
using BNKaraoke.DJ.Views;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;

namespace BNKaraoke.DJ.ViewModels
{
    public partial class DJScreenViewModel
    {
        private System.Timers.Timer? _pollingTimer;
        private const int PollingIntervalMs = 10000;

        private async Task InitializeSignalRAsync(string eventId)
        {
            try
            {
                if (string.IsNullOrEmpty(eventId) || !int.TryParse(eventId, out int parsedEventId))
                {
                    Log.Warning("[DJSCREEN SIGNALR] Cannot initialize SignalR: EventId is null, empty, or invalid");
                    StartPolling(eventId ?? "");
                    return;
                }

                if (_signalRService == null)
                {
                    Log.Warning("[DJSCREEN SIGNALR] Cannot initialize SignalR: SignalRService is null");
                    StartPolling(eventId);
                    return;
                }

                Log.Information("[DJSCREEN SIGNALR] Initializing SignalR connection for EventId={EventId}", eventId);
                await _signalRService.StartAsync(parsedEventId);
                StopPolling();
                Log.Information("[DJSCREEN SIGNALR] SignalR initialized for EventId={EventId}", eventId);
            }
            catch (SignalRException ex)
            {
                Log.Error("[DJSCREEN SIGNALR] Failed to initialize SignalR for EventId={EventId}: {Message}. Starting fallback polling.", eventId, ex.Message);
                StartPolling(eventId ?? "");
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN SIGNALR] Unexpected error initializing SignalR for EventId={EventId}: {Message}, StackTrace={StackTrace}", eventId, ex.Message, ex.StackTrace);
                StartPolling(eventId ?? "");
            }
        }

        private void StartPolling(string eventId)
        {
            if (_pollingTimer != null)
            {
                Log.Information("[DJSCREEN SIGNALR] Polling already active for EventId={EventId}", eventId);
                return;
            }

            Log.Information("[DJSCREEN SIGNALR] Starting fallback polling for EventId={EventId}", eventId);
            _pollingTimer = new System.Timers.Timer(PollingIntervalMs);
            _pollingTimer.Elapsed += async (s, e) => await PollDataAsync(eventId);
            _pollingTimer.AutoReset = true;
            _pollingTimer.Start();
        }

        private async Task PollDataAsync(string eventId)
        {
            try
            {
                Log.Information("[DJSCREEN SIGNALR] Polling queue and singer data for EventId={EventId}", eventId);
                await LoadQueueData();
                await LoadSingersAsync();
                await LoadSungCountAsync();
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN SIGNALR] Failed to poll data for EventId={EventId}: {Message}, StackTrace={StackTrace}", eventId, ex.Message, ex.StackTrace);
            }
        }

        private void StopPolling()
        {
            if (_pollingTimer != null)
            {
                Log.Information("[DJSCREEN SIGNALR] Stopping fallback polling");
                _pollingTimer.Stop();
                _pollingTimer.Dispose();
                _pollingTimer = null;
            }
        }

        private async Task<int> LoadSungCountAsync()
        {
            if (string.IsNullOrEmpty(_currentEventId))
            {
                return 0;
            }
            try
            {
                Log.Information("[DJSCREEN] Loading sung count for EventId={EventId}", _currentEventId);
                SungCount = await _apiService.GetSungCountAsync(_currentEventId);
                Log.Information("[DJSCREEN] Loaded sung count {Count} for EventId={EventId}", SungCount, _currentEventId);
                Application.Current.Dispatcher.Invoke(() => OnPropertyChanged(nameof(SungCount)));
                return SungCount;
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to load sung count for EventId={EventId}: {Message}", _currentEventId, ex.Message);
                Application.Current.Dispatcher.Invoke(() => SetWarningMessage($"Failed to load sung count: {ex.Message}"));
                return 0;
            }
        }

        [RelayCommand]
        private void ToggleShow() // Removed async, changed to void
        {
            try
            {
                Log.Information("[DJSCREEN] ToggleShow command invoked");
                if (_isDisposing) return;
                if (string.IsNullOrEmpty(_currentEventId))
                {
                    Log.Information("[DJSCREEN] ToggleShow failed: No event joined");
                    SetWarningMessage("Please join an event before starting the show.");
                    return;
                }

                if (!IsShowActive)
                {
                    Log.Information("[DJSCREEN] Starting show");
                    if (_videoPlayerWindow == null)
                    {
                        _videoPlayerWindow = new VideoPlayerWindow();
                        _videoPlayerWindow.SongEnded += VideoPlayerWindow_SongEnded;
                        _videoPlayerWindow.Closed += VideoPlayerWindow_Closed;
                        Log.Information("[DJSCREEN] Subscribed to SongEnded and Closed events for VideoPlayerWindow");
                    }
                    _videoPlayerWindow.Show();
                    IsShowActive = true;
                    ShowButtonText = "Stop Show";
                    ShowButtonColor = "#dc2626";
                    Log.Information("[DJSCREEN] Show started, VideoPlayerWindow shown with idle title");
                }
                else
                {
                    Log.Information("[DJSCREEN] Stopping show");
                    if (_videoPlayerWindow != null)
                    {
                        _videoPlayerWindow.Close();
                        _videoPlayerWindow = null;
                        Log.Information("[DJSCREEN] Closed VideoPlayerWindow");
                    }
                    if (_updateTimer != null)
                    {
                        _updateTimer.Stop();
                        _updateTimer = null;
                        Log.Information("[DJSCREEN] Stopped update timer");
                    }
                    if (_countdownTimer != null)
                    {
                        _countdownTimer.Stop();
                        _countdownTimer = null;
                        Log.Information("[DJSCREEN] Stopped countdown timer");
                    }
                    IsPlaying = false;
                    IsVideoPaused = false;
                    PlayingQueueEntry = null;
                    SliderPosition = 0;
                    CurrentVideoPosition = "--:--";
                    TimeRemainingSeconds = 0;
                    TimeRemaining = "0:00";
                    IsShowActive = false;
                    ShowButtonText = "Start Show";
                    ShowButtonColor = "#22d3ee";
                    OnPropertyChanged(nameof(PlayingQueueEntry));
                    OnPropertyChanged(nameof(SliderPosition));
                    OnPropertyChanged(nameof(CurrentVideoPosition));
                    OnPropertyChanged(nameof(TimeRemaining));
                    OnPropertyChanged(nameof(TimeRemainingSeconds));
                    Log.Information("[DJSCREEN] Show stopped, state reset");
                }
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to toggle show: {Message}", ex.Message);
                SetWarningMessage($"Failed to toggle show: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task LoginLogout()
        {
            try
            {
                Log.Information("[DJSCREEN] LoginLogout command invoked");
                if (_isDisposing) return;
                if (_userSessionService.IsAuthenticated)
                {
                    Log.Information("[DJSCREEN] Showing logout confirmation");
                    var result = MessageBox.Show("Are you sure you want to logout?", "Confirm Logout", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (result == MessageBoxResult.Yes)
                    {
                        Log.Information("[DJSCREEN] Logging out");
                        if (!string.IsNullOrEmpty(_currentEventId) && int.TryParse(_currentEventId, out int eventId) && _signalRService != null)
                        {
                            await _signalRService.StopAsync(eventId);
                            Log.Information("[DJSCREEN SIGNALR] Stopped SignalR connection for EventId={EventId}", _currentEventId);
                        }
                        StopPolling();
                        if (!string.IsNullOrEmpty(_currentEventId))
                        {
                            try
                            {
                                await _apiService.LeaveEventAsync(_currentEventId, _userSessionService.UserName ?? string.Empty);
                                Log.Information("[DJSCREEN] Left event: {EventId}", _currentEventId);
                            }
                            catch (Exception ex)
                            {
                                Log.Error("[DJSCREEN] Failed to leave event: {EventId}: {Message}", _currentEventId, ex.Message);
                                SetWarningMessage($"Failed to leave event: {ex.Message}");
                            }
                            _currentEventId = null;
                        }
                        _userSessionService.ClearSession();
                        if (_videoPlayerWindow != null)
                        {
                            _videoPlayerWindow.Close();
                            _videoPlayerWindow = null;
                            IsShowActive = false;
                            ShowButtonText = "Start Show";
                            ShowButtonColor = "#22d3ee";
                        }
                        await UpdateAuthenticationState();
                        Log.Information("[DJSCREEN] Logout complete: IsAuthenticated={IsAuthenticated}, WelcomeMessage={WelcomeMessage}, LoginLogoutButtonText={LoginLogoutButtonText}",
                            IsAuthenticated, WelcomeMessage, LoginLogoutButtonText);
                    }
                }
                else
                {
                    Log.Information("[DJSCREEN] Showing LoginWindow");
                    _isLoginWindowOpen = true;
                    try
                    {
                        var loginWindow = new LoginWindow { WindowStartupLocation = WindowStartupLocation.CenterScreen };
                        var result = loginWindow.ShowDialog();
                        _isLoginWindowOpen = false;
                        if (result == true)
                        {
                            await UpdateAuthenticationState();
                            Log.Information("[DJSCREEN] LoginWindow closed with successful login: IsAuthenticated={IsAuthenticated}, WelcomeMessage={WelcomeMessage}, LoginLogoutButtonText={LoginLogoutButtonText}",
                                IsAuthenticated, WelcomeMessage, LoginLogoutButtonText);
                        }
                        else
                        {
                            Log.Information("[DJSCREEN] LoginWindow closed without login");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error("[DJSCREEN] Failed to show LoginWindow: {Message}", ex.Message);
                        SetWarningMessage($"Failed to show login: {ex.Message}");
                    }
                    finally
                    {
                        _isLoginWindowOpen = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to process LoginLogout: {Message}", ex.Message);
                SetWarningMessage($"Failed to process login/logout: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task JoinLiveEvent()
        {
            Log.Information("[DJSCREEN] JoinLiveEvent command invoked");
            if (string.IsNullOrEmpty(_currentEventId))
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    var events = await _apiService.GetLiveEventsAsync(cts.Token);
                    if (events.Count == 0)
                    {
                        Log.Information("[DJSCREEN] No live events available");
                        JoinEventButtonText = "No Live Events";
                        JoinEventButtonColor = "Gray";
                        SetWarningMessage("No live events are currently available.");
                        return;
                    }

                    var eventDto = events.First();
                    if (string.IsNullOrEmpty(_userSessionService.UserName))
                    {
                        Log.Error("[DJSCREEN] Cannot join event: UserName is empty");
                        SetWarningMessage("Cannot join event: User username is not set.");
                        return;
                    }
                    await _apiService.JoinEventAsync(eventDto.EventId.ToString(), _userSessionService.UserName);
                    _currentEventId = eventDto.EventId.ToString();
                    CurrentEvent = eventDto;
                    JoinEventButtonText = $"Leave {eventDto.EventCode}";
                    JoinEventButtonColor = "#FF0000";
                    Log.Information("[DJSCREEN] Joined event: {EventId}, {EventCode}", _currentEventId, eventDto.EventCode);

                    await LoadSingersAsync();
                    await LoadQueueData();
                    await LoadSungCountAsync();
                    await InitializeSignalRAsync(_currentEventId);
                }
                catch (Exception ex)
                {
                    Log.Error("[DJSCREEN] Failed to join event: {Message}", ex.Message);
                    SetWarningMessage($"Failed to join event: {ex.Message}");
                }
            }
            else
            {
                try
                {
                    if (string.IsNullOrEmpty(_userSessionService.UserName))
                    {
                        Log.Error("[DJSCREEN] Cannot leave event: UserName is empty");
                        SetWarningMessage("Cannot leave event: User username is not set.");
                        return;
                    }
                    if (!string.IsNullOrEmpty(_currentEventId) && int.TryParse(_currentEventId, out int eventId) && _signalRService != null)
                    {
                        await _signalRService.StopAsync(eventId);
                        Log.Information("[DJSCREEN SIGNALR] Stopped SignalR connection for EventId={EventId}", _currentEventId);
                    }
                    StopPolling();
                    await _apiService.LeaveEventAsync(_currentEventId, _userSessionService.UserName);
                    Log.Information("[DJSCREEN] Left event: {EventId}", _currentEventId);
                    _currentEventId = null;
                    CurrentEvent = null;
                    QueueEntries.Clear();
                    Singers.Clear();
                    GreenSingers.Clear();
                    YellowSingers.Clear();
                    OrangeSingers.Clear();
                    RedSingers.Clear();
                    NonDummySingersCount = 0;
                    SungCount = 0;
                    if (_videoPlayerWindow != null)
                    {
                        _videoPlayerWindow.Close();
                        _videoPlayerWindow = null;
                        IsShowActive = false;
                        ShowButtonText = "Start Show";
                        ShowButtonColor = "#22d3ee";
                    }
                    PlayingQueueEntry = null;
                    OnPropertyChanged(nameof(NonDummySingersCount));
                    OnPropertyChanged(nameof(SungCount));
                    await UpdateAuthenticationState();
                }
                catch (Exception ex)
                {
                    Log.Error("[DJSCREEN] Failed to leave event: {EventId}: {Message}", _currentEventId, ex.Message);
                    SetWarningMessage($"Failed to leave event: {ex.Message}");
                }
            }
        }

        [RelayCommand]
        private void OpenSettings()
        {
            Log.Information("[DJSCREEN] Settings button clicked");
            try
            {
                var settingsWindow = new SettingsWindow { WindowStartupLocation = WindowStartupLocation.CenterScreen };
                settingsWindow.ShowDialog();
                Log.Information("[DJSCREEN] SettingsWindow closed");
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to open SettingsWindow: {Message}", ex.Message);
                SetWarningMessage($"Failed to open settings: {ex.Message}");
            }
        }

        private async Task UpdateJoinEventButtonState()
        {
            try
            {
                Log.Information("[DJSCREEN] Updating join event button state");
                if (!IsAuthenticated)
                {
                    JoinEventButtonText = "No Live Events";
                    JoinEventButtonColor = "Gray";
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        OnPropertyChanged(nameof(JoinEventButtonText));
                        OnPropertyChanged(nameof(JoinEventButtonColor));
                    });
                    return;
                }

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var events = await _apiService.GetLiveEventsAsync(cts.Token);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (events.Count == 0)
                    {
                        JoinEventButtonText = "No Live Events";
                        JoinEventButtonColor = "Gray";
                    }
                    else if (events.Count == 1)
                    {
                        JoinEventButtonText = string.IsNullOrEmpty(_currentEventId) ? $"Join {events[0].EventCode}" : $"Leave {events[0].EventCode}";
                        JoinEventButtonColor = string.IsNullOrEmpty(_currentEventId) ? "#3B82F6" : "#FF0000";
                    }
                    else
                    {
                        JoinEventButtonText = "Join Live Event";
                        JoinEventButtonColor = "#3B82F6";
                    }
                    Log.Information("[DJSCREEN] Join event button updated: JoinEventButtonText={JoinEventButtonText}, JoinEventButtonColor={JoinEventButtonColor}, EventCount={EventCount}",
                        JoinEventButtonText, JoinEventButtonColor, events.Count);

                    OnPropertyChanged(nameof(JoinEventButtonText));
                    OnPropertyChanged(nameof(JoinEventButtonColor));
                });
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to update join event button state: {Message}", ex.Message);
                SetWarningMessage($"Failed to update event button: {ex.Message}");
                Application.Current.Dispatcher.Invoke(() =>
                {
                    JoinEventButtonText = "No Live Events";
                    JoinEventButtonColor = "Gray";
                    OnPropertyChanged(nameof(JoinEventButtonText));
                    OnPropertyChanged(nameof(JoinEventButtonColor));
                });
            }
        }
    }
}