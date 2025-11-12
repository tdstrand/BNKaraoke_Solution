using BNKaraoke.DJ.Models;
using BNKaraoke.DJ.Services;
using BNKaraoke.DJ.Views;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace BNKaraoke.DJ.ViewModels
{
    public partial class DJScreenViewModel
    {
        private async Task InitializeSignalRAsync(string? eventId)
        {
            try
            {
                if (string.IsNullOrEmpty(eventId) || !int.TryParse(eventId, out int parsedEventId))
                {
                    Log.Warning("[DJSCREEN SIGNALR] Cannot initialize SignalR: EventId is null, empty, or invalid");
                    return;
                }
                Log.Information("[DJSCREEN SIGNALR] Initializing SignalR connection for EventId={EventId}", eventId);
                if (_signalRService != null)
                {
                    ResetInitialSnapshotTrackers();
                    await _signalRService.StartAsync(parsedEventId);
                    Log.Information("[DJSCREEN SIGNALR] SignalR initialized for EventId={EventId}", eventId);
                }
                else
                {
                    Log.Warning("[DJSCREEN SIGNALR] SignalRService is null; waiting for reconnect/pushed state for EventId={EventId}", eventId);
                }
            }
            catch (SignalRException ex)
            {
                Log.Error("[DJSCREEN SIGNALR] Failed to initialize SignalR for EventId={EventId}: {Message}, StackTrace={StackTrace}", eventId, ex.Message, ex.StackTrace);
                Log.Warning("[SIGNALR] Connection startup failed; waiting for reconnect/pushed state");
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN SIGNALR] Unexpected error initializing SignalR for EventId={EventId}: {Message}, StackTrace={StackTrace}", eventId, ex.Message, ex.StackTrace);
                Log.Warning("[SIGNALR] Connection startup failed; waiting for reconnect/pushed state");
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
                    var switchingEvent = _joinedEventId.HasValue && _joinedEventId.Value != eventId;
                    var rejoiningSameEvent = _joinedEventId.HasValue && _joinedEventId.Value == eventId;

                    _hasReceivedInitialQueue = false;
                    _isHydratingFromSignalR = true;

                    await _apiService.JoinEventAsync(eventId.ToString(), _userSessionService.UserName);
                    _joinedEventId = eventId;
                    _currentEventId = eventId.ToString();
                    CurrentEvent = selectedEvent;
                    JoinEventButtonText = $"Leave {eventCode}";
                    JoinEventButtonColor = "#FF0000";
                    IsJoinEventButtonEnabled = true;
                    SetViewSungSongsVisibility(true);
                    Log.Information("[DJSCREEN] Joined event: {EventId}, {EventCode}", _currentEventId, eventCode);
                    if (switchingEvent)
                    {
                        ClearQueueCollections("JoinLiveEvent(SwitchingEvent)");
                    }

                    if (!rejoiningSameEvent)
                    {
                        Singers.Clear();
                        GreenSingers.Clear();
                        YellowSingers.Clear();
                        OrangeSingers.Clear();
                        RedSingers.Clear();
                    }
                    if (_currentEventId != null)
                    {
                        await _apiService.ResetNowPlayingAsync(_currentEventId);
                        _queueUpdateMetadata.Clear();
                        _singerUpdateMetadata.Clear();
                        await InitializeSignalRAsync(_currentEventId);
                    }
                    await EnsureInitialSnapshotsAsync();
                }
                else
                {
                    if (string.IsNullOrEmpty(_userSessionService.UserName))
                    {
                        Log.Error("[DJSCREEN] Cannot leave event: UserName is empty");
                        SetWarningMessage("Cannot leave event: Username is not set.");
                        return;
                    }
                    await LeaveCurrentEventAsync("JoinLiveEvent", true);
                }
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to join/leave event: {Message}, StackTrace={StackTrace}", ex.Message, ex.StackTrace);
                SetWarningMessage($"Failed to join/leave event: {ex.Message}");
                _isHydratingFromSignalR = false;
            }
        }

        private async Task LeaveCurrentEventAsync(string context, bool refreshLiveEvents)
        {
            if (string.IsNullOrEmpty(_currentEventId))
            {
                Log.Information("[DJSCREEN] {Context}: No active event to leave", context);
                SetViewSungSongsVisibility(false);
                return;
            }

            var eventId = _currentEventId;
            try
            {
                if (_signalRService != null && int.TryParse(eventId, out int parsedEventId))
                {
                    await _signalRService.StopAsync(parsedEventId);
                    Log.Information("[DJSCREEN SIGNALR] {Context}: Stopped SignalR connection for EventId={EventId}", context, eventId);
                }
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN SIGNALR] {Context}: Failed to stop SignalR connection for EventId={EventId}: {Message}, StackTrace={StackTrace}",
                    context, eventId, ex.Message, ex.StackTrace);
            }

            if (!string.IsNullOrEmpty(_userSessionService.UserName))
            {
                try
                {
                    await _apiService.LeaveEventAsync(eventId, _userSessionService.UserName);
                    Log.Information("[DJSCREEN] {Context}: Left event: {EventId}", context, eventId);
                }
                catch (Exception ex)
                {
                    Log.Error("[DJSCREEN] {Context}: Failed to leave event: {EventId}: {Message}, StackTrace={StackTrace}", context, eventId, ex.Message, ex.StackTrace);
                    SetWarningMessage($"Failed to leave event: {ex.Message}");
                }
            }
            else
            {
                Log.Warning("[DJSCREEN] {Context}: Cannot leave event {EventId}: Username is not set", context, eventId);
            }

            _currentEventId = null;
            CurrentEvent = null;
            SelectedEvent = null;
            _joinedEventId = null;
            _isHydratingFromSignalR = false;
            _hasReceivedInitialQueue = false;
            _queueUpdateMetadata.Clear();
            _singerUpdateMetadata.Clear();
            _initialQueueTcs = null;
            _initialSingersTcs = null;
            _queueDebounceTimer?.Stop();
            _singerDebounceTimer?.Stop();
            ClearQueueCollections();
            Singers.Clear();
            GreenSingers.Clear();
            YellowSingers.Clear();
            OrangeSingers.Clear();
            RedSingers.Clear();
            NonDummySingersCount = 0;
            SungCount = 0;

            TeardownShowVisuals();
            SetPreShowButton();
            CurrentShowState = ShowState.PreShow;

            SetViewSungSongsVisibility(false);

            if (refreshLiveEvents)
            {
                await LoadLiveEventsAsync();
            }
        }

        private async Task LogoutWithoutConfirmationAsync(string context)
        {
            try
            {
                Log.Information("[DJSCREEN] {Context}: Performing logout without additional confirmation", context);

                if (!string.IsNullOrEmpty(_currentEventId))
                {
                    await LeaveCurrentEventAsync(context, false);
                }

                _userSessionService.ClearSession();

                TeardownShowVisuals();
                SetPreShowButton();
                CurrentShowState = ShowState.PreShow;
                Log.Information("[DJSCREEN] {Context}: Show visuals reset during logout", context);

                await UpdateAuthenticationState();
                SetViewSungSongsVisibility(false);

                Log.Information("[DJSCREEN] {Context}: Logout completed", context);
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] {Context}: Failed to logout: {Message}, StackTrace={StackTrace}", context, ex.Message, ex.StackTrace);
                SetWarningMessage($"Failed to logout: {ex.Message}");
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
                    var result = MessageBox.Show("Are you sure you want to logout?", "Confirm Logout", MessageBoxButton.YesNo, MessageBoxImage.None);
                    if (result == MessageBoxResult.Yes)
                    {
                        await LogoutWithoutConfirmationAsync("LogoutCommand");
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
        private async Task ExitApplication()
        {
            try
            {
                Log.Information("[DJSCREEN] Exit command invoked");
                var result = MessageBox.Show("Are you sure you want to exit BNKaraoke DJ?", "Confirm Exit", MessageBoxButton.YesNo, MessageBoxImage.None);
                if (result != MessageBoxResult.Yes)
                {
                    Log.Information("[DJSCREEN] Exit cancelled by user");
                    return;
                }

                await StopSongIfPlayingAsync();

                if (IsShowActive)
                {
                    await ToggleShow();
                }
                else if (_videoPlayerWindow != null)
                {
                    TeardownShowVisuals();
                    SetPreShowButton();
                    CurrentShowState = ShowState.PreShow;
                    Log.Information("[DJSCREEN] Show visuals torn down prior to shutdown");
                }

                await LeaveCurrentEventAsync("ExitCommand", false);

                if (_userSessionService.IsAuthenticated)
                {
                    await LogoutWithoutConfirmationAsync("ExitCommand");
                }

                Dispose();

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var windows = Application.Current.Windows.Cast<Window>().ToList();
                    foreach (var window in windows)
                    {
                        try
                        {
                            window.Close();
                        }
                        catch (Exception ex)
                        {
                            Log.Error("[DJSCREEN] Failed to close window during exit: {Message}, StackTrace={StackTrace}", ex.Message, ex.StackTrace);
                        }
                    }

                    Application.Current.Shutdown();
                });

                Log.Information("[DJSCREEN] Application shutdown initiated by Exit command");
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to exit application: {Message}, StackTrace={StackTrace}", ex.Message, ex.StackTrace);
                MessageBox.Show($"Failed to exit application: {ex.Message}", "Exit Error", MessageBoxButton.OK, MessageBoxImage.None);
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