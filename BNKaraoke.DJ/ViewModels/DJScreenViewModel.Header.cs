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
using System.Windows.Controls;

namespace BNKaraoke.DJ.ViewModels
{
    public partial class DJScreenViewModel
    {
        private System.Timers.Timer? _pollingTimer;
        private const int PollingIntervalMs = 10000;

        private async Task InitializeSignalRAsync(string? eventId)
        {
            try
            {
                if (string.IsNullOrEmpty(eventId) || !int.TryParse(eventId, out int parsedEventId))
                {
                    Log.Warning("[DJSCREEN SIGNALR] Cannot initialize SignalR: EventId is null, empty, or invalid");
                    StartPolling(eventId ?? "");
                    return;
                }
                Log.Information("[DJSCREEN SIGNALR] Initializing SignalR connection for EventId={EventId}", eventId);
                if (_signalRService != null)
                {
                    await _signalRService.StartAsync(parsedEventId);
                    StopPolling();
                    Log.Information("[DJSCREEN SIGNALR] SignalR initialized for EventId={EventId}", eventId);
                }
                else
                {
                    Log.Warning("[DJSCREEN SIGNALR] SignalRService is null, starting fallback polling for EventId={EventId}", eventId);
                    StartPolling(eventId);
                }
            }
            catch (SignalRException ex)
            {
                Log.Error("[DJSCREEN SIGNALR] Failed to initialize SignalR for EventId={EventId}: {Message}, StackTrace={StackTrace}", eventId, ex.Message, ex.StackTrace);
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
                Log.Error("[DJSCREEN] Failed to load sung count for EventId={EventId}: {Message}, StackTrace={StackTrace}", _currentEventId, ex.Message, ex.StackTrace);
                Application.Current.Dispatcher.Invoke(() => SetWarningMessage($"Failed to load sung count: {ex.Message}"));
                return 0;
            }
        }

        [RelayCommand]
        private async Task JoinLiveEvent()
        {
            if (string.IsNullOrEmpty(_currentEventId))
            {
                if (LiveEvents.Count > 1)
                {
                    var selectorWindow = new EventSelectorWindow(this)
                    {
                        WindowStartupLocation = WindowStartupLocation.CenterScreen
                    };
                    var result = selectorWindow.ShowDialog();
                    if (result != true || SelectedEvent == null)
                    {
                        Log.Information("[DJSCREEN] JoinLiveEvent cancelled: No event selected in dialog");
                        return;
                    }
                }
                else if (LiveEvents.Count == 1)
                {
                    SelectedEvent = LiveEvents.FirstOrDefault();
                    if (SelectedEvent == null)
                    {
                        Log.Information("[DJSCREEN] JoinLiveEvent failed: No live events available");
                        SetWarningMessage("No live events available to join.");
                        return;
                    }
                }
                else
                {
                    Log.Information("[DJSCREEN] JoinLiveEvent failed: No live events available");
                    SetWarningMessage("No live events available to join.");
                    return;
                }
            }
            if (SelectedEvent == null)
            {
                Log.Information("[DJSCREEN] JoinLiveEvent failed: No event selected");
                SetWarningMessage("Please select an event to join.");
                return;
            }
            try
            {
                var selectedEvent = SelectedEvent; // Local variable to ensure non-null
                var eventId = selectedEvent.EventId; // Non-nullable int per EventDto
                var eventCode = selectedEvent.EventCode ?? "Unknown"; // Null-safe
                Log.Information("[DJSCREEN] JoinLiveEvent command invoked for EventId={EventId}", eventId);
                if (string.IsNullOrEmpty(_currentEventId))
                {
                    if (string.IsNullOrEmpty(_userSessionService.UserName))
                    {
                        Log.Error("[DJSCREEN] Cannot join event: UserName is empty");
                        SetWarningMessage("Cannot join event: Username is not set.");
                        return;
                    }
                    await _apiService.JoinEventAsync(eventId.ToString(), _userSessionService.UserName);
                    _currentEventId = eventId.ToString();
                    CurrentEvent = selectedEvent;
                    JoinEventButtonText = $"Leave {eventCode}";
                    JoinEventButtonColor = "#FF0000";
                    IsJoinEventButtonEnabled = true;
                    SetViewSungSongsVisibility(true);
                    Log.Information("[DJSCREEN] Joined event: {EventId}, {EventCode}", _currentEventId, eventCode);
                    if (_currentEventId != null)
                    {
                        await _apiService.ResetNowPlayingAsync(_currentEventId);
                        await InitializeSignalRAsync(_currentEventId);
                    }
                    QueueEntries.Clear();
                    Singers.Clear();
                    GreenSingers.Clear();
                    YellowSingers.Clear();
                    OrangeSingers.Clear();
                    RedSingers.Clear();
                    await LoadQueueData();
                    await LoadSingersAsync();
                    await LoadSungCountAsync();
                }
                else
                {
                    if (string.IsNullOrEmpty(_userSessionService.UserName))
                    {
                        Log.Error("[DJSCREEN] Cannot leave event: UserName is empty");
                        SetWarningMessage("Cannot leave event: Username is not set.");
                        return;
                    }
                    if (!string.IsNullOrEmpty(_currentEventId) && int.TryParse(_currentEventId, out int parsedEventId))
                    {
                        if (_signalRService != null)
                        {
                            await _signalRService.StopAsync(parsedEventId);
                            Log.Information("[DJSCREEN SIGNALR] Stopped SignalR connection for EventId={EventId}", _currentEventId);
                        }
                    }
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
                    ResetPlaybackState();
                    if (_videoPlayerWindow != null)
                    {
                        _videoPlayerWindow.Close();
                        _videoPlayerWindow = null;
                        IsShowActive = false;
                        ShowButtonText = "Start Show";
                        ShowButtonColor = "#22d3ee";
                        Log.Information("[DJSCREEN] Stopped video and closed VideoPlayerWindow on leave event");
                    }
                    await LoadLiveEventsAsync();
                    SetViewSungSongsVisibility(false);
                }
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to join/leave event: {Message}, StackTrace={StackTrace}", ex.Message, ex.StackTrace);
                SetWarningMessage($"Failed to join/leave event: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task LoginLogout()
        {
            try
            {
                Log.Information("[DJSCREEN] LoginLogout command invoked");
                if (_userSessionService.IsAuthenticated)
                {
                    Log.Information("[DJSCREEN] Showing logout confirmation");
                    var result = MessageBox.Show("Are you sure you want to logout?", "Confirm Logout", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (result == MessageBoxResult.Yes)
                    {
                        Log.Information("[DJSCREEN] Logging out");
                        if (!string.IsNullOrEmpty(_currentEventId) && int.TryParse(_currentEventId, out int eventId))
                        {
                            if (_signalRService != null)
                            {
                                await _signalRService.StopAsync(eventId);
                                Log.Information("[DJSCREEN SIGNALR] Stopped SignalR connection for EventId={EventId}", _currentEventId);
                            }
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
                                Log.Error("[DJSCREEN] Failed to leave event: {EventId}: {Message}, StackTrace={StackTrace}", _currentEventId, ex.Message, ex.StackTrace);
                                SetWarningMessage($"Failed to leave event: {ex.Message}");
                            }
                            _currentEventId = null;
                            SetViewSungSongsVisibility(false);
                        }
                        _userSessionService.ClearSession();
                        ResetPlaybackState();
                        if (_videoPlayerWindow != null)
                        {
                            _videoPlayerWindow.Close();
                            _videoPlayerWindow = null;
                            IsShowActive = false;
                            ShowButtonText = "Start Show";
                            ShowButtonColor = "#22d3ee";
                            Log.Information("[DJSCREEN] Stopped video and closed VideoPlayerWindow on logout");
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
                            if (string.IsNullOrEmpty(_userSessionService.UserName))
                            {
                                Log.Error("[DJSCREEN] Login failed: UserName is null");
                                SetWarningMessage("Login failed: Username not set.");
                                _userSessionService.ClearSession();
                            }
                            await UpdateAuthenticationState();
                            Log.Information("[DJSCREEN] LoginWindow closed with successful login: IsAuthenticated={IsAuthenticated}, WelcomeMessage={WelcomeMessage}, LoginLogoutButtonText={LoginLogoutButtonText}, UserName={UserName}",
                                IsAuthenticated, WelcomeMessage, LoginLogoutButtonText, _userSessionService.UserName ?? "null");
                        }
                        else
                        {
                            Log.Information("[DJSCREEN] LoginWindow closed without login");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error("[DJSCREEN] Failed to show LoginWindow: {Message}, StackTrace={StackTrace}", ex.Message, ex.StackTrace);
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
                Log.Error("[DJSCREEN] Failed to process LoginLogout: {Message}, StackTrace={StackTrace}", ex.Message, ex.StackTrace);
                SetWarningMessage($"Failed to process login/logout: {ex.Message}");
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
                Log.Error("[DJSCREEN] Failed to open SettingsWindow: {Message}, StackTrace={StackTrace}", ex.Message, ex.StackTrace);
                SetWarningMessage($"Failed to open settings: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task OpenCacheManager()
        {
            Log.Information("[DJSCREEN] Cache manager button clicked");
            try
            {
                var viewModel = new CacheManagerViewModel(_cacheSyncService);
                await viewModel.LoadAsync();
                var window = new CacheManagerWindow
                {
                    DataContext = viewModel,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };
                window.ShowDialog();
                Log.Information("[DJSCREEN] CacheManagerWindow closed");
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to open CacheManagerWindow: {Message}", ex.Message);
                SetWarningMessage($"Failed to open cache manager: {ex.Message}");
            }
        }

        private async Task UpdateJoinEventButtonState()
        {
            try
            {
                Log.Information("[DJSCREEN] Updating join event button state");
                if (!IsAuthenticated)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        JoinEventButtonText = "No Live Events";
                        JoinEventButtonColor = "Gray";
                        IsJoinEventButtonEnabled = false;
                        OnPropertyChanged(nameof(JoinEventButtonText));
                        OnPropertyChanged(nameof(JoinEventButtonColor));
                        OnPropertyChanged(nameof(IsJoinEventButtonEnabled));
                    });
                    return;
                }
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var events = await _apiService.GetLiveEventsAsync(cts.Token);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    UpdateJoinEventButtonState(events);
                });
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to update join event button state: {Message}, StackTrace={StackTrace}", ex.Message, ex.StackTrace);
                SetWarningMessage($"Failed to update event button: {ex.Message}");
                Application.Current.Dispatcher.Invoke(() =>
                {
                    JoinEventButtonText = "No Live Events";
                    JoinEventButtonColor = "Gray";
                    IsJoinEventButtonEnabled = false;
                    OnPropertyChanged(nameof(JoinEventButtonText));
                    OnPropertyChanged(nameof(JoinEventButtonColor));
                    OnPropertyChanged(nameof(IsJoinEventButtonEnabled));
                });
            }
        }
    }
}