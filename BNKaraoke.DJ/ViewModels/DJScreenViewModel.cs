using BNKaraoke.DJ.Models;
using BNKaraoke.DJ.Services;
using BNKaraoke.DJ.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace BNKaraoke.DJ.ViewModels
{
    public partial class DJScreenViewModel : ObservableObject
    {
        private readonly IUserSessionService _userSessionService = UserSessionService.Instance;
        private readonly IApiService _apiService = new ApiService(UserSessionService.Instance, SettingsService.Instance);
        private readonly SettingsService _settingsService = SettingsService.Instance;
        private readonly VideoCacheService? _videoCacheService;
        private readonly SignalRService? _signalRService;
        private string? _currentEventId;
        private VideoPlayerWindow? _videoPlayerWindow;
        private bool _isLoginWindowOpen;

        [ObservableProperty] private bool _isAuthenticated;
        [ObservableProperty] private string _welcomeMessage = "Not logged in";
        [ObservableProperty] private string _loginLogoutButtonText = "Login";
        [ObservableProperty] private string _loginLogoutButtonColor = "#3B82F6"; // Blue
        [ObservableProperty] private string _joinEventButtonText = "No Live Events";
        [ObservableProperty] private string _joinEventButtonColor = "Gray"; // Disabled
        [ObservableProperty] private bool _isJoinEventButtonVisible;
        [ObservableProperty] private bool _isJoinEventButtonEnabled;
        [ObservableProperty] private ObservableCollection<EventDto> _liveEvents = new ObservableCollection<EventDto>();
        [ObservableProperty] private EventDto? _selectedEvent;
        [ObservableProperty] private EventDto? _currentEvent;
        [ObservableProperty] private ObservableCollection<QueueEntry> _queueEntries = new ObservableCollection<QueueEntry>();
        [ObservableProperty] private QueueEntry? _selectedQueueEntry;
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
        [ObservableProperty] private QueueEntry? _playingQueueEntry;
        [ObservableProperty] private int _totalSongsPlayed;
        [ObservableProperty] private bool _isAutoPlayEnabled = true;
        [ObservableProperty] private string _autoPlayButtonText = "Auto Play: On";
        [ObservableProperty] private string _currentVideoPosition = "--:--";
        [ObservableProperty] private string _timeRemaining = "0:00";
        [ObservableProperty] private int _timeRemainingSeconds;
        [ObservableProperty] private DateTime? _warningExpirationTime;
        [ObservableProperty] private bool _isVideoPaused;
        [ObservableProperty] private string _warningMessage = "";
        [ObservableProperty] private int _sungCount;
        [ObservableProperty] private double _songPosition;
        [ObservableProperty] private TimeSpan _songDuration = TimeSpan.FromMinutes(4);
        [ObservableProperty] private string _stopRestartButtonColor = "#22d3ee"; // Default cyan

        public ICommand? ViewSungSongsCommand { get; }

        public DJScreenViewModel(VideoCacheService? videoCacheService = null)
        {
            try
            {
                Log.Information("[DJSCREEN VM] Starting ViewModel constructor");
                _videoCacheService = videoCacheService ?? new VideoCacheService(_settingsService);
                Log.Information("[DJSCREEN VM] VideoCacheService initialized, CachePath={CachePath}", _settingsService.Settings.VideoCachePath);
                _signalRService = new SignalRService(
                    _userSessionService,
                    (queueId, action, position, isOnBreak, isSingerLoggedIn, isSingerJoined, isSingerOnBreak) =>
                        HandleQueueUpdated(queueId, action, position, isOnBreak, isSingerLoggedIn, isSingerJoined, isSingerOnBreak),
                    (requestorUserName, isLoggedIn, isJoined, isOnBreak) =>
                        HandleSingerStatusUpdated(requestorUserName, isLoggedIn, isJoined, isOnBreak),
                    HandleInitialQueue,
                    HandleInitialSingers
                );
                _userSessionService.SessionChanged += UserSessionService_SessionChanged;
                Log.Information("[DJSCREEN VM] Subscribed to SessionChanged event");
                ViewSungSongsCommand = new RelayCommand(ExecuteViewSungSongs);
                UpdateAuthenticationStateInitial();
                LoadLiveEventsAsync().GetAwaiter().GetResult();
                Log.Information("[DJSCREEN VM] Initialized UI state in constructor");
                Log.Information("[DJSCREEN VM] ViewModel instance created: {InstanceId}", GetHashCode());
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN VM] Failed to initialize ViewModel: {Message}, StackTrace={StackTrace}", ex.Message, ex.StackTrace);
                MessageBox.Show($"Failed to initialize DJScreen: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
                    CurrentEvent = null;
                    LiveEvents.Clear();
                    SelectedEvent = null;
                    QueueEntries.Clear();
                    Singers.Clear();
                    GreenSingers.Clear();
                    YellowSingers.Clear();
                    OrangeSingers.Clear();
                    RedSingers.Clear();
                    NonDummySingersCount = 0;
                    SungCount = 0;
                    ClearPlayingQueueEntry();
                    if (_videoPlayerWindow != null)
                    {
                        _videoPlayerWindow.Close();
                        _videoPlayerWindow = null;
                        IsShowActive = false;
                        ShowButtonText = "Start Show";
                        ShowButtonColor = "#22d3ee";
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
                    OnPropertyChanged(nameof(QueueEntries));
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
                    QueueEntries.Clear();
                    Singers.Clear();
                    GreenSingers.Clear();
                    YellowSingers.Clear();
                    OrangeSingers.Clear();
                    RedSingers.Clear();
                    NonDummySingersCount = 0;
                    SungCount = 0;
                    ClearPlayingQueueEntry();
                    if (_videoPlayerWindow != null)
                    {
                        _videoPlayerWindow.Close();
                        _videoPlayerWindow = null;
                        IsShowActive = false;
                        ShowButtonText = "Start Show";
                        ShowButtonColor = "#22d3ee";
                    }
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
                    OnPropertyChanged(nameof(QueueEntries));
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
                    TimeRemaining = "0:00";
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

        private void HandleQueueUpdated(int queueId, string action, int? position, bool? isOnBreak, bool isSingerLoggedIn, bool isSingerJoined, bool isSingerOnBreak)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    Log.Information("[DJSCREEN SIGNALR] Handling QueueUpdated: QueueId={QueueId}, Action={Action}, Position={Position}, IsOnBreak={IsOnBreak}, IsSingerLoggedIn={IsSingerLoggedIn}, IsSingerJoined={IsSingerJoined}, IsSingerOnBreak={IsSingerOnBreak}",
                        queueId, action, position, isOnBreak, isSingerLoggedIn, isSingerJoined, isSingerOnBreak);
                    var queueEntry = QueueEntries.FirstOrDefault(q => q.QueueId == queueId);
                    if (queueEntry != null)
                    {
                        queueEntry.Position = position ?? queueEntry.Position;
                        queueEntry.IsOnBreak = isOnBreak ?? queueEntry.IsOnBreak;
                        queueEntry.IsSingerLoggedIn = isSingerLoggedIn;
                        queueEntry.IsSingerJoined = isSingerJoined;
                        queueEntry.IsSingerOnBreak = isSingerOnBreak;
                        OnPropertyChanged(nameof(QueueEntries));
                    }
                    if (action == "Added" || action == "Removed" || action == "Moved")
                    {
                        LoadQueueData().GetAwaiter().GetResult();
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("[DJSCREEN SIGNALR] Failed to handle QueueUpdated for QueueId={QueueId}: {Message}, StackTrace={StackTrace}", queueId, ex.Message, ex.StackTrace);
                    SetWarningMessage($"Failed to update queue: {ex.Message}");
                }
            });
        }

        private void HandleSingerStatusUpdated(string requestorUserName, bool isLoggedIn, bool isJoined, bool isOnBreak)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    Log.Information("[DJSCREEN SIGNALR] Handling SingerStatusUpdated: RequestorUserName={RequestorUserName}, IsLoggedIn={IsLoggedIn}, IsJoined={IsJoined}, IsOnBreak={IsOnBreak}",
                        requestorUserName, isLoggedIn, isJoined, isOnBreak);
                    LoadSingersAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Log.Error("[DJSCREEN SIGNALR] Failed to handle SingerStatusUpdated for RequestorUserName={RequestorUserName}: {Message}, StackTrace={StackTrace}", requestorUserName, ex.Message, ex.StackTrace);
                    SetWarningMessage($"Failed to update singers: {ex.Message}");
                }
            });
        }

        private void HandleInitialQueue(List<QueueEntry> queue)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                QueueEntries.Clear();
                foreach (var item in queue)
                {
                    QueueEntries.Add(item);
                }
                Log.Information("[DJSCREEN SIGNALR] Initial queue loaded: Count={Count}", QueueEntries.Count);
            });
        }

        private void HandleInitialSingers(List<Singer> singers)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Singers.Clear();
                GreenSingers.Clear();
                YellowSingers.Clear();
                OrangeSingers.Clear();
                RedSingers.Clear();
                foreach (var singer in singers)
                {
                    Singers.Add(singer);
                    if (singer.IsJoined && !singer.IsOnBreak)
                        GreenSingers.Add(singer);
                    else if (singer.IsOnBreak)
                        YellowSingers.Add(singer);
                    else if (singer.IsLoggedIn)
                        OrangeSingers.Add(singer);
                    else
                        RedSingers.Add(singer);
                }
                NonDummySingersCount = Singers.Count(s => !string.IsNullOrEmpty(s.DisplayName));
                Log.Information("[DJSCREEN SIGNALR] Initial singers loaded: Total={Total}, Green={Green}, Yellow={Yellow}, Orange={Orange}, Red={Red}",
                    Singers.Count, GreenSingers.Count, YellowSingers.Count, OrangeSingers.Count, RedSingers.Count);
            });
        }

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
            private readonly Action<object?> _execute;
            public RelayCommand(Action<object?> execute)
            {
                _execute = execute;
            }
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter) => _execute(parameter);
#pragma warning disable CS0067 // Suppress unused event warning
            public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
        }
    }
}