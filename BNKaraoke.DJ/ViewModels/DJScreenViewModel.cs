using BNKaraoke.DJ.Models;
using BNKaraoke.DJ.Services;
using BNKaraoke.DJ.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace BNKaraoke.DJ.ViewModels
{
    public enum ShowState
    {
        PreShow,
        Running,
        Paused,
        Ended
    }

    public partial class DJScreenViewModel : ObservableObject
    {
        private readonly IUserSessionService _userSessionService = UserSessionService.Instance;
        private readonly IApiService _apiService = new ApiService(UserSessionService.Instance, SettingsService.Instance);
        private readonly SettingsService _settingsService = SettingsService.Instance;
        private readonly VideoCacheService? _videoCacheService;
        private readonly SignalRService? _signalRService;
        private readonly CacheSyncService _cacheSyncService = null!;
        private readonly Dictionary<string, SingerUpdateMetadata> _singerUpdateMetadata = new(StringComparer.OrdinalIgnoreCase);
        private DateTime _lastRulesUpdate = DateTime.MinValue;
        private const int RulesThrottleMs = 500;
        private DispatcherTimer? _queueDebounceTimer;
        private DispatcherTimer? _singerDebounceTimer;
        private TaskCompletionSource<bool>? _initialQueueTcs;
        private TaskCompletionSource<bool>? _initialSingersTcs;
        private readonly TimeSpan _initialSnapshotTimeout = TimeSpan.FromSeconds(5);
        private string? _currentEventId;
        private VideoPlayerWindow? _videoPlayerWindow;
        private int _preFadeVolume = 100;
        private bool _isLoginWindowOpen;
        private int? _joinedEventId;
        private bool _isHydratingFromSignalR;
        private bool _hasReceivedInitialQueue;

        [ObservableProperty] private bool _isAuthenticated;
        [ObservableProperty] private string _welcomeMessage = "Not logged in";
        [ObservableProperty] private string _loginLogoutButtonText = "Login";
        [ObservableProperty] private string _loginLogoutButtonColor = "#3B82F6"; // Blue
        [ObservableProperty] private string _joinEventButtonText = "No Live Events";
        [ObservableProperty] private string _joinEventButtonColor = "Gray"; // Disabled
        [ObservableProperty] private bool _isJoinEventButtonVisible;
        [ObservableProperty] private bool _isJoinEventButtonEnabled;
        [ObservableProperty] private bool _isViewSungSongsButtonVisible;
        [ObservableProperty] private ObservableCollection<EventDto> _liveEvents = new ObservableCollection<EventDto>();
        [ObservableProperty] private EventDto? _selectedEvent;
        [ObservableProperty] private EventDto? _currentEvent;
        [ObservableProperty] private QueueEntryViewModel? _selectedQueueEntry;
        [ObservableProperty] private bool _isPlaying;
        [ObservableProperty] private ObservableCollection<Singer> _singers = new ObservableCollection<Singer>();
        [ObservableProperty] private int _nonDummySingersCount;
        [ObservableProperty] private ObservableCollection<Singer> _greenSingers = new ObservableCollection<Singer>();
        [ObservableProperty] private ObservableCollection<Singer> _yellowSingers = new ObservableCollection<Singer>();
        [ObservableProperty] private ObservableCollection<Singer> _orangeSingers = new ObservableCollection<Singer>();
        [ObservableProperty] private ObservableCollection<Singer> _redSingers = new ObservableCollection<Singer>();
        [ObservableProperty] private string _showButtonText = "Start Show";
        [ObservableProperty] private string _showButtonColor = "#22d3ee"; // Cyan
        [ObservableProperty] private bool _isShowActive;
        [ObservableProperty] private QueueEntryViewModel? _playingQueueEntry;
        [ObservableProperty] private int _totalSongsPlayed;
        [ObservableProperty] private bool _isAutoPlayEnabled = false;
        [ObservableProperty] private string _autoPlayButtonText = "Auto Play: OFF";
        [ObservableProperty] private string _currentVideoPosition = "--:--";
        [ObservableProperty] private string _timeRemaining = "--:--";
        [ObservableProperty] private int _timeRemainingSeconds;
        [ObservableProperty] private DateTime? _warningExpirationTime;
        [ObservableProperty] private bool _isVideoPaused;
        [ObservableProperty] private string _warningMessage = "";
        [ObservableProperty] private int _sungCount;
        [ObservableProperty] private double _songPosition;
        [ObservableProperty] private TimeSpan _songDuration = TimeSpan.FromMinutes(4);
        [ObservableProperty] private string _stopRestartButtonColor = "#22d3ee"; // Default cyan
        [ObservableProperty] private string _normalizationDisplay = "0.0";
        [ObservableProperty] private int _bassBoost; // Bass gain in dB (0-20)
        [ObservableProperty] private ShowState _currentShowState = ShowState.PreShow;

        private readonly RelayCommand _getReorderSuggestionsCommand;

        public ICommand? ViewSungSongsCommand { get; }
        public ICommand IncreaseBassBoostCommand { get; } = null!;
        public ICommand DecreaseBassBoostCommand { get; } = null!;
        public IAsyncRelayCommand ManualRefreshDataCommand { get; } = null!;
        public ICommand GetReorderSuggestionsCommand => _getReorderSuggestionsCommand;

        public DJScreenViewModel(VideoCacheService? videoCacheService = null)
        {
            _getReorderSuggestionsCommand = new RelayCommand(
                async () => await GetReorderSuggestionsAsync(),
                () => QueueEntriesInternal.Count >= 3 && CurrentEvent?.EventId > 0);

            try
            {
                Log.Information("[DJSCREEN VM] Starting ViewModel constructor");
                _videoCacheService = videoCacheService ?? new VideoCacheService(_settingsService, _apiService);
                Log.Information("[DJSCREEN VM] VideoCacheService initialized, CachePath={CachePath}", _settingsService.Settings.VideoCachePath);
                _signalRService = new SignalRService(
                    _userSessionService,
                    HandleQueueItemAdded,
                    HandleQueueItemUpdated,
                    HandleQueueItemRemoved,
                    HandleQueueReorderApplied,
                    HandleSingerStatusUpdated,
                    HandleInitialQueue,
                    HandleInitialSingers
                );
                _userSessionService.SessionChanged += UserSessionService_SessionChanged;
                Log.Information("[DJSCREEN VM] Subscribed to SessionChanged event");
                ViewSungSongsCommand = new RelayCommand(ExecuteViewSungSongs);
                IncreaseBassBoostCommand = new RelayCommand(_ => BassBoost = Math.Min(20, BassBoost + 1));
                DecreaseBassBoostCommand = new RelayCommand(_ => BassBoost = Math.Max(0, BassBoost - 1));
                ManualRefreshDataCommand = new AsyncRelayCommand(ManualRefreshDataAsync);
                _joinedEventId = null;
                _isHydratingFromSignalR = false;
                _hasReceivedInitialQueue = false;
                UpdateAuthenticationStateInitial();
                LoadLiveEventsAsync().GetAwaiter().GetResult();
                IsAutoPlayEnabled = false;
                UpdateReorderCommandState();
                Log.Information("[DJSCREEN VM] Initialized UI state in constructor");
                Log.Information("[DJSCREEN VM] ViewModel instance created: {InstanceId}", GetHashCode());
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN VM] Failed to initialize ViewModel: {Message}, StackTrace={StackTrace}", ex.Message, ex.StackTrace);
                MessageBox.Show($"Failed to initialize DJScreen: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.None);
            }
        }

        private void SetViewSungSongsVisibility(bool isVisible)
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (IsViewSungSongsButtonVisible != isVisible)
                    {
                        IsViewSungSongsButtonVisible = isVisible;
                        OnPropertyChanged(nameof(IsViewSungSongsButtonVisible));
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to update View Sung Songs visibility: {Message}", ex.Message);
            }
        }

        private void SetPreShowButton()
        {
            IsShowActive = false;
            ShowButtonText = "Start Show";
            ShowButtonColor = "#22d3ee";
        }

        private void SetLiveShowButton()
        {
            IsShowActive = true;
            ShowButtonText = "End Show";
            ShowButtonColor = "#FF0000";
        }

        private async Task LoadLiveEventsAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var events = await _apiService.GetLiveEventsAsync(cts.Token);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    LiveEvents.Clear();
                    foreach (var evt in events.Where(e => e != null && !string.IsNullOrEmpty(e.EventCode) && e.Status == "Live"))
                    {
                        LiveEvents.Add(evt);
                    }
                    UpdateJoinEventButtonState(events);
                    if (LiveEvents.Any() && SelectedEvent == null)
                    {
                        SelectedEvent = LiveEvents.FirstOrDefault(); // Ensure default selection
                    }
                    OnPropertyChanged(nameof(LiveEvents));
                    OnPropertyChanged(nameof(SelectedEvent));
                    OnPropertyChanged(nameof(JoinEventButtonText));
                    OnPropertyChanged(nameof(JoinEventButtonColor));
                    OnPropertyChanged(nameof(IsJoinEventButtonEnabled));
                });
                Log.Information("[DJSCREEN VM] Loaded {Count} live events", LiveEvents.Count);
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN VM] Failed to load live events: {Message}, StackTrace={StackTrace}", ex.Message, ex.StackTrace);
                SetWarningMessage($"Failed to load live events: {ex.Message}");
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

        private async Task GetReorderSuggestionsAsync()
        {
            if (CurrentEvent?.EventId is not int eventId || eventId <= 0)
            {
                MessageBox.Show("Not joined to an event.", "Reorder", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                Log.Information("[REORDER] Fetching suggestions for EventId={EventId}", eventId);
                var response = await _apiService.GetReorderSuggestionsAsync(eventId);

                if (response?.Suggestions?.Any() != true)
                {
                    MessageBox.Show("Queue is already fair!", "Reorder", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var dialog = new ReorderSuggestionsWindow(response.Suggestions);
                if (dialog.ShowDialog() == true)
                {
                    await ApplyReorderSuggestionsAsync(response.Suggestions);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[REORDER] Failed to fetch suggestions for EventId={EventId}", eventId);
                MessageBox.Show($"Error: {ex.Message}", "Reorder", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ApplyReorderSuggestionsAsync(List<ReorderSuggestion> suggestions)
        {
            if (CurrentEvent?.EventId is not int eventId || eventId <= 0)
            {
                MessageBox.Show("Not joined to an event.", "Reorder", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var request = new ApplyReorderRequest
                {
                    EventId = eventId,
                    Suggestions = suggestions
                };

                await _apiService.ApplyReorderSuggestionsAsync(request);
                Log.Information("[REORDER] Applied {SuggestionCount} suggestions for EventId={EventId}", suggestions.Count, eventId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[REORDER] Failed to apply suggestions for EventId={EventId}", eventId);
                MessageBox.Show($"Error applying reorder suggestions: {ex.Message}", "Reorder Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateReorderCommandState()
        {
            _getReorderSuggestionsCommand?.RaiseCanExecuteChanged();
        }

        private void UpdateJoinEventButtonState(IReadOnlyList<EventDto> events)
        {
            if (!IsAuthenticated)
            {
                JoinEventButtonText = "No Live Events";
                JoinEventButtonColor = "Gray";
                IsJoinEventButtonEnabled = false;
                SelectedEvent = null;
                return;
            }
            var liveEvents = events.Where(e => e != null && !string.IsNullOrEmpty(e.EventCode) && e.Status == "Live").ToList();
            if (liveEvents.Count == 0)
            {
                JoinEventButtonText = "No Live Events";
                JoinEventButtonColor = "Gray";
                IsJoinEventButtonEnabled = false;
                SelectedEvent = null;
            }
            else if (liveEvents.Count == 1)
            {
                JoinEventButtonText = $"Join Event: {liveEvents[0].EventCode ?? "Event"}";
                JoinEventButtonColor = "#3B82F6";
                IsJoinEventButtonEnabled = true;
                SelectedEvent = liveEvents[0];
            }
            else
            {
                JoinEventButtonText = "Join Event: Select";
                JoinEventButtonColor = "#3B82F6";
                IsJoinEventButtonEnabled = true;
                SelectedEvent = liveEvents.FirstOrDefault(); // Default to first valid event
            }
        }

        private void ExecuteViewSungSongs(object? parameter)
        {
            try
            {
                Log.Information("[DJSCREEN] ViewSungSongs command invoked");
                if (string.IsNullOrEmpty(_currentEventId))
                {
                    Log.Information("[DJSCREEN] ViewSungSongs failed: No event joined");
                    SetWarningMessage("Please join an event to view sung songs.");
                    return;
                }
                var viewModel = new SungSongsViewModel(_apiService, _currentEventId);
                var window = new SungSongsView
                {
                    DataContext = viewModel
                };
                window.Loaded += async (s, e) =>
                {
                    await viewModel.InitializeAsync();
                    Log.Information("[DJSCREEN] Initialized SungSongsViewModel for EventId={EventId}", _currentEventId);
                };
                window.ShowDialog();
                Log.Information("[DJSCREEN] SungSongsView opened for EventId={EventId}", _currentEventId);
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to open SungSongsView: {Message}, StackTrace={StackTrace}", ex.Message, ex.StackTrace);
                SetWarningMessage($"Failed to view sung songs: {ex.Message}");
            }
        }

        public void UpdateAuthenticationStateInitial()
        {
            try
            {
                Log.Information("[DJSCREEN] Initializing authentication state");
                bool newIsAuthenticated = _userSessionService.IsAuthenticated;
                string newWelcomeMessage = newIsAuthenticated ? $"Welcome, {_userSessionService.FirstName ?? "User"}" : "Not logged in";
                string newLoginLogoutButtonText = newIsAuthenticated ? "Logout" : "Login";
                string newLoginLogoutButtonColor = newIsAuthenticated ? "#FF0000" : "#3B82F6";
                bool newIsJoinEventButtonVisible = newIsAuthenticated;
                IsAuthenticated = newIsAuthenticated;
                WelcomeMessage = newWelcomeMessage;
                LoginLogoutButtonText = newLoginLogoutButtonText;
                LoginLogoutButtonColor = newLoginLogoutButtonColor;
                IsJoinEventButtonVisible = newIsJoinEventButtonVisible;
                if (!newIsAuthenticated)
                {
                    JoinEventButtonText = "No Live Events";
                    JoinEventButtonColor = "Gray";
                    IsJoinEventButtonEnabled = false;
                    _currentEventId = null;
                    _joinedEventId = null;
                    _isHydratingFromSignalR = false;
                    _hasReceivedInitialQueue = false;
                    CurrentEvent = null;
                    LiveEvents.Clear();
                    SelectedEvent = null;
                    ClearQueueCollections();
                    Singers.Clear();
                    GreenSingers.Clear();
                    YellowSingers.Clear();
                    OrangeSingers.Clear();
                    RedSingers.Clear();
                    NonDummySingersCount = 0;
                    SungCount = 0;
                    ClearPlayingQueueEntry();
                    TeardownShowVisuals();
                    SetPreShowButton();
                    CurrentShowState = ShowState.PreShow;
                }
                Application.Current.Dispatcher.Invoke(() =>
                {
                    OnPropertyChanged(nameof(IsAuthenticated));
                    OnPropertyChanged(nameof(WelcomeMessage));
                    OnPropertyChanged(nameof(LoginLogoutButtonText));
                    OnPropertyChanged(nameof(LoginLogoutButtonColor));
                    OnPropertyChanged(nameof(IsJoinEventButtonVisible));
                    OnPropertyChanged(nameof(JoinEventButtonText));
                    OnPropertyChanged(nameof(JoinEventButtonColor));
                    OnPropertyChanged(nameof(IsJoinEventButtonEnabled));
                    OnPropertyChanged(nameof(LiveEvents));
                    OnPropertyChanged(nameof(SelectedEvent));
                    OnPropertyChanged(nameof(CurrentEvent));
                    OnPropertyChanged(nameof(Singers));
                    OnPropertyChanged(nameof(GreenSingers));
                    OnPropertyChanged(nameof(YellowSingers));
                    OnPropertyChanged(nameof(OrangeSingers));
                    OnPropertyChanged(nameof(RedSingers));
                    OnPropertyChanged(nameof(NonDummySingersCount));
                    OnPropertyChanged(nameof(ShowButtonText));
                    OnPropertyChanged(nameof(ShowButtonColor));
                    OnPropertyChanged(nameof(IsShowActive));
                    OnPropertyChanged(nameof(PlayingQueueEntry));
                    OnPropertyChanged(nameof(SungCount));
                });
                SetViewSungSongsVisibility(!string.IsNullOrEmpty(_currentEventId));
                Log.Information("[DJSCREEN] Initial authentication state set: IsAuthenticated={IsAuthenticated}, WelcomeMessage={WelcomeMessage}, LoginLogoutButtonText={LoginLogoutButtonText}, IsJoinEventButtonVisible={IsJoinEventButtonVisible}, UserName={UserName}",
                    IsAuthenticated, WelcomeMessage, LoginLogoutButtonText, IsJoinEventButtonVisible, _userSessionService.UserName ?? "null");
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to initialize authentication state: {Message}, StackTrace={StackTrace}", ex.Message, ex.StackTrace);
                SetWarningMessage($"Failed to initialize authentication: {ex.Message}");
            }
        }

        public async Task UpdateAuthenticationState()
        {
            try
            {
                Log.Information("[DJSCREEN] Updating authentication state");
                bool newIsAuthenticated = _userSessionService.IsAuthenticated;
                string newWelcomeMessage = newIsAuthenticated ? $"Welcome, {_userSessionService.FirstName ?? "User"}" : "Not logged in";
                string newLoginLogoutButtonText = newIsAuthenticated ? "Logout" : "Login";
                string newLoginLogoutButtonColor = newIsAuthenticated ? "#FF0000" : "#3B82F6";
                bool newIsJoinEventButtonVisible = newIsAuthenticated;
                IsAuthenticated = newIsAuthenticated;
                WelcomeMessage = newWelcomeMessage;
                LoginLogoutButtonText = newLoginLogoutButtonText;
                LoginLogoutButtonColor = newLoginLogoutButtonColor;
                IsJoinEventButtonVisible = newIsJoinEventButtonVisible;
                if (!newIsAuthenticated)
                {
                    JoinEventButtonText = "No Live Events";
                    JoinEventButtonColor = "Gray";
                    IsJoinEventButtonEnabled = false;
                    _currentEventId = null;
                    CurrentEvent = null;
                    LiveEvents.Clear();
                    SelectedEvent = null;
                    ClearQueueCollections();
                    Singers.Clear();
                    GreenSingers.Clear();
                    YellowSingers.Clear();
                    OrangeSingers.Clear();
                    RedSingers.Clear();
                    NonDummySingersCount = 0;
                    SungCount = 0;
                    ClearPlayingQueueEntry();
                    TeardownShowVisuals();
                    SetPreShowButton();
                    CurrentShowState = ShowState.PreShow;
                    if (_signalRService != null)
                    {
                        await _signalRService.StopAsync(0);
                        Log.Information("[DJSCREEN SIGNALR] Disconnected from KaraokeDJHub on logout");
                    }
                }
                else
                {
                    if (string.IsNullOrEmpty(_userSessionService.UserName))
                    {
                        Log.Warning("[DJSCREEN] UserName is null after authentication");
                        SetWarningMessage("User authentication incomplete: UserName not set.");
                        _userSessionService.ClearSession();
                        IsAuthenticated = false;
                        WelcomeMessage = "Not logged in";
                        LoginLogoutButtonText = "Login";
                        LoginLogoutButtonColor = "#3B82F6";
                        JoinEventButtonText = "No Live Events";
                        JoinEventButtonColor = "Gray";
                        IsJoinEventButtonEnabled = false;
                    }
                    else if (string.IsNullOrEmpty(_currentEventId) && !_isLoginWindowOpen)
                    {
                        await LoadLiveEventsAsync();
                    }
                }
                Application.Current.Dispatcher.Invoke(() =>
                {
                    OnPropertyChanged(nameof(IsAuthenticated));
                    OnPropertyChanged(nameof(WelcomeMessage));
                    OnPropertyChanged(nameof(LoginLogoutButtonText));
                    OnPropertyChanged(nameof(LoginLogoutButtonColor));
                    OnPropertyChanged(nameof(IsJoinEventButtonVisible));
                    OnPropertyChanged(nameof(JoinEventButtonText));
                    OnPropertyChanged(nameof(JoinEventButtonColor));
                    OnPropertyChanged(nameof(IsJoinEventButtonEnabled));
                    OnPropertyChanged(nameof(LiveEvents));
                    OnPropertyChanged(nameof(SelectedEvent));
                    OnPropertyChanged(nameof(CurrentEvent));
                    OnPropertyChanged(nameof(Singers));
                    OnPropertyChanged(nameof(GreenSingers));
                    OnPropertyChanged(nameof(YellowSingers));
                    OnPropertyChanged(nameof(OrangeSingers));
                    OnPropertyChanged(nameof(RedSingers));
                    OnPropertyChanged(nameof(NonDummySingersCount));
                    OnPropertyChanged(nameof(ShowButtonText));
                    OnPropertyChanged(nameof(ShowButtonColor));
                    OnPropertyChanged(nameof(IsShowActive));
                    OnPropertyChanged(nameof(PlayingQueueEntry));
                    OnPropertyChanged(nameof(SungCount));
                });
                SetViewSungSongsVisibility(!string.IsNullOrEmpty(_currentEventId));
                Log.Information("[DJSCREEN] Authentication state updated: IsAuthenticated={IsAuthenticated}, WelcomeMessage={WelcomeMessage}, LoginLogoutButtonText={LoginLogoutButtonText}, IsJoinEventButtonVisible={IsJoinEventButtonVisible}, UserName={UserName}",
                    IsAuthenticated, WelcomeMessage, LoginLogoutButtonText, IsJoinEventButtonVisible, _userSessionService.UserName ?? "null");
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to update authentication state: {Message}, StackTrace={StackTrace}", ex.Message, ex.StackTrace);
                SetWarningMessage($"Failed to update authentication: {ex.Message}");
            }
        }

        private void ClearPlayingQueueEntry()
        {
            try
            {
                Log.Information("[DJSCREEN] Clearing PlayingQueueEntry");
                Application.Current.Dispatcher.Invoke(() =>
                {
                    PlayingQueueEntry = null;
                    IsPlaying = false;
                    IsVideoPaused = false;
                    SongPosition = 0;
                    CurrentVideoPosition = "--:--";
                    TimeRemainingSeconds = 0;
                    TimeRemaining = "--:--";
                    StopRestartButtonColor = "#22d3ee";
                    OnPropertyChanged(nameof(PlayingQueueEntry));
                    OnPropertyChanged(nameof(IsPlaying));
                    OnPropertyChanged(nameof(IsVideoPaused));
                    OnPropertyChanged(nameof(SongPosition));
                    OnPropertyChanged(nameof(CurrentVideoPosition));
                    OnPropertyChanged(nameof(TimeRemaining));
                    OnPropertyChanged(nameof(TimeRemainingSeconds));
                    OnPropertyChanged(nameof(StopRestartButtonColor));
                    Log.Information("[DJSCREEN] UI updated after clearing PlayingQueueEntry");
                });
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to clear PlayingQueueEntry: {Message}, StackTrace={StackTrace}", ex.Message, ex.StackTrace);
                SetWarningMessage($"Failed to clear current song: {ex.Message}");
            }
        }

        private void HandleQueueReorderApplied(QueueReorderAppliedMessage message)
        {
            Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    if (string.IsNullOrEmpty(_currentEventId) || !int.TryParse(_currentEventId, out var currentEventId) || currentEventId != message.EventId)
                    {
                        Log.Information("[DJSCREEN SIGNALR] Ignoring queue/reorder_applied for EventId={MessageEventId}. CurrentEventId={CurrentEventId}", message.EventId, _currentEventId);
                        return;
                    }

                    var movedCount = message.MovedQueueIds?.Count ?? message.Metrics?.MoveCount ?? 0;
                    Log.Information("[DJSCREEN SIGNALR] Processing queue/reorder_applied: EventId={EventId}, Version={Version}, Moves={Moves}", message.EventId, message.Version, movedCount);

                    var order = message.Order ?? new List<QueueReorderOrderItem>();
                    var queueMap = _queueEntryLookup.Values.ToDictionary(entry => entry.QueueId);
                    var reordered = new List<QueueEntryViewModel>(queueMap.Count);

                    foreach (var item in order.OrderBy(o => o.Position))
                    {
                        if (queueMap.TryGetValue(item.QueueId, out var entry))
                        {
                            entry.Position = item.Position;
                            reordered.Add(entry);
                            queueMap.Remove(item.QueueId);
                        }
                        else
                        {
                            Log.Debug("[DJSCREEN SIGNALR] queue/reorder_applied included unknown QueueId={QueueId}", item.QueueId);
                        }
                    }

                    if (queueMap.Count > 0)
                    {
                        foreach (var leftover in queueMap.Values.OrderBy(e => e.Position))
                        {
                            reordered.Add(leftover);
                        }
                    }

                    var previouslySelectedId = SelectedQueueEntry?.QueueId;

                    QueueEntriesInternal.Clear();
                    _hiddenQueueEntryIds.Clear();
                    foreach (var entry in reordered.OrderBy(e => e.Position))
                    {
                        UpdateEntryVisibility(entry);
                    }

                    LogQueueSummary("Updated");

                    if (previouslySelectedId.HasValue)
                    {
                        var restoredSelection = QueueEntriesInternal.FirstOrDefault(q => q.QueueId == previouslySelectedId.Value);
                        if (restoredSelection != null)
                        {
                            SelectedQueueEntry = restoredSelection;
                        }
                        else if (QueueEntriesInternal.Any())
                        {
                            SelectedQueueEntry = QueueEntriesInternal.First();
                        }
                    }

                    await LoadSungCountAsync();

                    if (movedCount > 0)
                    {
                        var toast = movedCount == 1
                            ? "Queue reorder applied: 1 song moved."
                            : $"Queue reorder applied: {movedCount} songs moved.";
                        SetWarningMessage(toast);
                    }
                    else
                    {
                        SetWarningMessage("Queue reorder applied.");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[DJSCREEN SIGNALR] Failed to handle queue/reorder_applied for EventId={EventId}: {Message}", message.EventId, ex.Message);
                    SetWarningMessage($"Failed to process reorder update: {ex.Message}");
                }
            });
        }

        private void HandleSingerStatusUpdated(SingerStatusUpdateMessage message)
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    Log.Information("[DJSCREEN SIGNALR] Handling SingerStatusUpdated: UserId={UserId}, IsLoggedIn={IsLoggedIn}, IsJoined={IsJoined}, IsOnBreak={IsOnBreak}, UpdateId={UpdateId}",
                        message.UserId, message.IsLoggedIn, message.IsJoined, message.IsOnBreak, message.UpdateId);

                    if (ShouldIgnoreSingerUpdate(message))
                    {
                        Log.Warning("[DJSCREEN SIGNALR] Ignored duplicate/out-of-order singer update for UserId={UserId}", message.UserId);
                        return;
                    }

                    ApplySingerUpdate(message);
                    UpdateSingerMetadata(message);
                    ScheduleSingerAggregation();
                }
                catch (Exception ex)
                {
                    Log.Error("[DJSCREEN SIGNALR] Failed to handle SingerStatusUpdated for UserId={UserId}: {Message}, StackTrace={StackTrace}",
                        message.UserId, ex.Message, ex.StackTrace);
                    SetWarningMessage($"Failed to update singers: {ex.Message}");
                }
            });
        }

        private async Task ManualRefreshDataAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(_currentEventId))
                {
                    Log.Information("[DJSCREEN] Manual refresh skipped: no active event");
                    SetWarningMessage("Join an event before refreshing data.");
                    return;
                }

                Log.Information("[DJSCREEN] Manual refresh invoked for EventId={EventId}", _currentEventId);
                await LoadQueueData();
                await LoadSingersAsync();
                await LoadSungCountAsync();
                Log.Warning("[DJSCREEN QUEUE] REFRESH FORCED via manual refresh");
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Manual refresh failed: {Message}, StackTrace={StackTrace}", ex.Message, ex.StackTrace);
                SetWarningMessage($"Refresh failed: {ex.Message}");
            }
        }

        private void ScheduleQueueReevaluation()
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_queueDebounceTimer == null)
                    {
                        _queueDebounceTimer = new DispatcherTimer(DispatcherPriority.Background)
                        {
                            Interval = TimeSpan.FromMilliseconds(100)
                        };
                    }
                    else
                    {
                        _queueDebounceTimer.Interval = TimeSpan.FromMilliseconds(100);
                    }
                    _queueDebounceTimer.Stop();
                    _queueDebounceTimer.Tick -= QueueDebounceTimerOnTick;
                    _queueDebounceTimer.Tick += QueueDebounceTimerOnTick;
                    _queueDebounceTimer.Start();
                });
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to schedule queue reevaluation: {Message}", ex.Message);
            }
        }

        private void QueueDebounceTimerOnTick(object? sender, EventArgs e)
        {
            if (_queueDebounceTimer == null)
            {
                return;
            }

            _queueDebounceTimer.Stop();
            SyncQueueSingerStatuses();
        }

        private void ScheduleSingerAggregation()
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_singerDebounceTimer == null)
                    {
                        _singerDebounceTimer = new DispatcherTimer(DispatcherPriority.Background)
                        {
                            Interval = TimeSpan.FromMilliseconds(100)
                        };
                    }
                    else
                    {
                        _singerDebounceTimer.Interval = TimeSpan.FromMilliseconds(100);
                    }
                    _singerDebounceTimer.Stop();
                    _singerDebounceTimer.Tick -= SingerDebounceTimerOnTick;
                    _singerDebounceTimer.Tick += SingerDebounceTimerOnTick;
                    _singerDebounceTimer.Start();
                });
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to schedule singer aggregation: {Message}", ex.Message);
            }
        }

        private void SingerDebounceTimerOnTick(object? sender, EventArgs e)
        {
            if (_singerDebounceTimer == null)
            {
                return;
            }

            _singerDebounceTimer.Stop();
            SortSingers();
            SyncQueueSingerStatuses();
        }

        private bool ShouldIgnoreSingerUpdate(SingerStatusUpdateMessage message)
        {
            if (string.IsNullOrEmpty(message.UserId))
            {
                return false;
            }

            if (!_singerUpdateMetadata.TryGetValue(message.UserId, out var metadata))
            {
                return false;
            }

            if (message.UpdateId.HasValue && metadata.UpdateId.HasValue && message.UpdateId == metadata.UpdateId)
            {
                return true;
            }

            if (message.UpdatedAtUtc.HasValue && metadata.UpdatedAtUtc.HasValue && message.UpdatedAtUtc <= metadata.UpdatedAtUtc)
            {
                return true;
            }

            return false;
        }

        private void UpdateSingerMetadata(SingerStatusUpdateMessage message)
        {
            if (string.IsNullOrEmpty(message.UserId))
            {
                return;
            }

            if (!_singerUpdateMetadata.TryGetValue(message.UserId, out var metadata))
            {
                metadata = new SingerUpdateMetadata();
                _singerUpdateMetadata[message.UserId] = metadata;
            }

            if (message.UpdateId.HasValue)
            {
                metadata.UpdateId = message.UpdateId;
            }
            if (message.UpdatedAtUtc.HasValue)
            {
                metadata.UpdatedAtUtc = message.UpdatedAtUtc;
            }
        }

        private void RefreshQueueOrdering()
        {
            var ordered = QueueEntriesInternal.OrderBy(q => q.Position).ToList();
            QueueEntriesInternal.Clear();
            foreach (var entry in ordered)
            {
                QueueEntriesInternal.Add(entry);
            }
        }

        private void ApplyQueueRules()
        {
            var totalCount = _queueEntryLookup.Count;
            Log.Information("[DJSCREEN QUEUE] ApplyQueueRules starting: tracked={Total}", totalCount);

            // REUSE EXISTING COLLECTION - DO NOT CREATE NEW INSTANCE
            // This preserves XAML ListView binding
            QueueEntriesInternal.Clear();

            var visibleEntries = new List<QueueEntryViewModel>();
            var sungCount = 0;
            var skippedCount = 0;

            foreach (var entry in _queueEntryLookup.Values)
            {
                if (entry == null)
                {
                    continue;
                }

                if (entry.SungAt != null)
                {
                    sungCount++;
                }

                if (entry.WasSkipped)
                {
                    skippedCount++;
                }

                if (IsVisiblyQueued(entry))
                {
                    visibleEntries.Add(entry);
                }
            }

            visibleEntries = visibleEntries
                .OrderBy(q => q.Position)
                .ToList();

            Log.Information(
                "[DJSCREEN QUEUE] ApplyQueueRules: total={Total}, visible={Visible}, sung={Sung}, skipped={Skipped}",
                totalCount,
                visibleEntries.Count,
                sungCount,
                skippedCount);

            foreach (var entry in visibleEntries)
            {
                QueueEntriesInternal.Add(entry);
            }

            // No need for OnPropertyChanged - ObservableCollection raises it automatically
        }

        private void ApplyV2QueueItem(QueueEntryViewModel entry, DJQueueItemDto dto)
        {
            entry.QueueId = dto.QueueId;
            entry.EventId = dto.EventId;
            entry.SongId = dto.SongId;
            entry.SongTitle = dto.SongTitle;
            entry.SongArtist = dto.SongArtist;
            entry.YouTubeUrl = dto.YouTubeUrl;
            entry.RequestorUserName = dto.RequestorUserName;
            entry.RequestorDisplayName = !string.IsNullOrWhiteSpace(dto.RequestorDisplayName)
                ? dto.RequestorDisplayName
                : dto.Singer?.DisplayName ?? dto.RequestorUserName;
            entry.Singers = dto.Singers != null ? new List<string>(dto.Singers) : new List<string>();
            entry.Position = dto.Position;
            entry.Status = dto.Status;
            entry.IsActive = dto.IsActive;
            entry.WasSkipped = dto.WasSkipped;
            entry.IsCurrentlyPlaying = dto.IsCurrentlyPlaying;
            entry.SungAt = dto.SungAt;
            entry.IsOnBreak = dto.Singer?.IsOnBreak ?? dto.IsSingerOnBreak;
            entry.IsOnHold = !string.IsNullOrWhiteSpace(dto.HoldReason) && !string.Equals(dto.HoldReason, "None", StringComparison.OrdinalIgnoreCase);
            entry.IsUpNext = dto.IsUpNext;
            entry.HoldReason = string.IsNullOrWhiteSpace(dto.HoldReason) || string.Equals(dto.HoldReason, "None", StringComparison.OrdinalIgnoreCase)
                ? null
                : dto.HoldReason;
            entry.IsSingerLoggedIn = dto.IsSingerLoggedIn;
            entry.IsSingerJoined = dto.IsSingerJoined;
            entry.IsSingerOnBreak = dto.IsSingerOnBreak;
            entry.IsServerCached = dto.IsServerCached;
            entry.IsMature = dto.IsMature;
            entry.NormalizationGain = dto.NormalizationGain;
            entry.FadeStartTime = dto.FadeStartTime;
            entry.IntroMuteDuration = dto.IntroMuteDuration;
            entry.ApplySingerStatus(dto.Singer);
            entry.UpdateStatusBrush();
        }

        private void ApplySingerUpdate(SingerStatusUpdateMessage message)
        {
            if (string.IsNullOrEmpty(message.UserId))
            {
                Log.Warning("[DJSCREEN SIGNALR] Singer update missing UserId; ignoring");
                return;
            }

            var singer = Singers.FirstOrDefault(s => s.UserId.Equals(message.UserId, StringComparison.OrdinalIgnoreCase));
            if (singer == null)
            {
                singer = new Singer
                {
                    UserId = message.UserId,
                    DisplayName = message.DisplayName ?? message.UserId,
                    UpdatedAt = DateTime.UtcNow
                };
                Singers.Add(singer);
                Log.Information("[DJSCREEN SIGNALR] Added singer {UserId} via SignalR", message.UserId);
            }

            if (!string.IsNullOrEmpty(message.DisplayName))
            {
                singer.DisplayName = message.DisplayName;
            }

            singer.IsLoggedIn = message.IsLoggedIn;
            singer.IsJoined = message.IsJoined;
            singer.IsOnBreak = message.IsOnBreak;
            singer.UpdatedAt = message.UpdatedAtUtc ?? DateTime.UtcNow;
            singer.UpdateStatusFlagsFromBooleans(singer.IsLoggedIn, singer.IsJoined, singer.IsOnBreak);
        }

        private void ResetInitialSnapshotTrackers()
        {
            try
            {
                _initialQueueTcs?.TrySetCanceled();
                _initialSingersTcs?.TrySetCanceled();
            }
            catch (Exception ex)
            {
                Log.Warning("[DJSCREEN] Failed to cancel previous snapshot trackers: {Message}", ex.Message);
            }

            _initialQueueTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _initialSingersTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private Task<bool> WaitForInitialQueueSnapshotAsync()
        {
            return _initialQueueTcs != null
                ? WaitForSnapshotAsync(_initialQueueTcs.Task, "queue")
                : Task.FromResult(false);
        }

        private Task<bool> WaitForInitialSingerSnapshotAsync()
        {
            return _initialSingersTcs != null
                ? WaitForSnapshotAsync(_initialSingersTcs.Task, "singers")
                : Task.FromResult(false);
        }

        private async Task<bool> WaitForSnapshotAsync(Task<bool> snapshotTask, string snapshotName)
        {
            var completedTask = await Task.WhenAny(snapshotTask, Task.Delay(_initialSnapshotTimeout));
            if (completedTask == snapshotTask)
            {
                return await snapshotTask;
            }

            Log.Warning("[DJSCREEN SIGNALR] Timed out waiting for initial {Snapshot} snapshot", snapshotName);
            return false;
        }

        private async Task EnsureInitialSnapshotsAsync()
        {
            var queueTask = WaitForInitialQueueSnapshotAsync();
            var singerTask = WaitForInitialSingerSnapshotAsync();

            var queueReceived = await queueTask;
            if (!queueReceived)
            {
                Log.Warning("[DJSCREEN SIGNALR] Falling back to REST queue snapshot for EventId={EventId}", _currentEventId);
                await LoadQueueData();
                _initialQueueTcs?.TrySetResult(true);
            }

            var singersReceived = await singerTask;
            if (!singersReceived)
            {
                Log.Warning("[DJSCREEN SIGNALR] Falling back to REST singers snapshot for EventId={EventId}", _currentEventId);
                await LoadSingersAsync();
                _initialSingersTcs?.TrySetResult(true);
            }

            await LoadSungCountAsync();
            ScheduleQueueReevaluation();
            ScheduleSingerAggregation();
        }

        private void TryCompleteHydration(string context)
        {
            if (_isHydratingFromSignalR && _hasReceivedInitialQueue)
            {
                _isHydratingFromSignalR = false;
                Log.Information("[DJSCREEN SIGNALR] Hydration completed via {Context}. EventId={EventId}", context, _currentEventId ?? "<none>");
            }
        }

        // Initial queue and singer handlers are defined in partial classes

        private async void UserSessionService_SessionChanged(object? sender, EventArgs e)
        {
            try
            {
                Log.Information("[DJSCREEN] Session changed event received");
                if (!_isLoginWindowOpen)
                {
                    await UpdateAuthenticationState();
                }
                else
                {
                    Log.Information("[DJSCREEN] Skipped UpdateAuthenticationState due to open LoginWindow");
                }
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to handle session changed event: {Message}, StackTrace={StackTrace}", ex.Message, ex.StackTrace);
                SetWarningMessage($"Failed to handle session change: {ex.Message}");
            }
        }

        private class RelayCommand : ICommand
        {
            private readonly Action<object?>? _execute;
            private readonly Func<object?, Task>? _executeAsync;
            private readonly Func<bool>? _canExecute;
            private bool _isExecuting;

            public RelayCommand(Action<object?> execute)
                : this(execute, null)
            {
            }

            public RelayCommand(Action<object?> execute, Func<bool>? canExecute)
            {
                _execute = execute ?? throw new ArgumentNullException(nameof(execute));
                _canExecute = canExecute;
            }

            public RelayCommand(Func<Task> execute, Func<bool>? canExecute)
            {
                if (execute == null)
                {
                    throw new ArgumentNullException(nameof(execute));
                }

                _executeAsync = _ => execute();
                _canExecute = canExecute;
            }

            public bool CanExecute(object? parameter)
            {
                return !_isExecuting && (_canExecute?.Invoke() ?? true);
            }

            public async void Execute(object? parameter)
            {
                if (!CanExecute(parameter))
                {
                    return;
                }

                if (_executeAsync != null)
                {
                    try
                    {
                        _isExecuting = true;
                        RaiseCanExecuteChanged();
                        await _executeAsync(parameter);
                    }
                    finally
                    {
                        _isExecuting = false;
                        RaiseCanExecuteChanged();
                    }
                }
                else
                {
                    _execute?.Invoke(parameter);
                }
            }

            public event EventHandler? CanExecuteChanged;

            public void RaiseCanExecuteChanged()
            {
                if (Application.Current?.Dispatcher?.CheckAccess() == true)
                {
                    CanExecuteChanged?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    Application.Current?.Dispatcher?.Invoke(() => CanExecuteChanged?.Invoke(this, EventArgs.Empty));
                }
            }
        }

        private sealed class SingerUpdateMetadata
        {
            public Guid? UpdateId { get; set; }
            public DateTime? UpdatedAtUtc { get; set; }
        }
    }
}
