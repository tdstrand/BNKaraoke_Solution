using BNKaraoke.DJ.Models;
using BNKaraoke.DJ.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Serilog;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace BNKaraoke.DJ.ViewModels
{
    public class SongDetailsViewModel : ObservableObject
    {
        private readonly IUserSessionService _userSessionService;
        private readonly SettingsService _settingsService;
        private QueueEntry? _selectedQueueEntry;
        private string _songId = "N/A";
        private string _songTitle = "N/A";
        private string _songArtist = "N/A";
        private string _genre = "N/A";
        private string _status = "N/A";
        private string _mood = "N/A";
        private string _serverCached = "N/A";
        private string _matureContent = "N/A";
        private string _gainValue = "0.0 dB";
        private string _fadeOutStart = "--:--";
        private string _introMute = "--:--";
        private string _songUrl = "N/A";

        public QueueEntry? SelectedQueueEntry
        {
            get => _selectedQueueEntry;
            set
            {
                if (SetProperty(ref _selectedQueueEntry, value) && value != null)
                {
                    SongId = value.SongId.ToString();
                    SongTitle = value.SongTitle ?? "N/A";
                    SongArtist = value.SongArtist ?? "N/A";
                    Genre = value.Genre ?? "N/A";
                    SongUrl = string.IsNullOrWhiteSpace(value.YouTubeUrl) ? "N/A" : value.YouTubeUrl!;
                }
            }
        }

        public string SongId
        {
            get => _songId;
            set => SetProperty(ref _songId, value);
        }

        public string SongTitle
        {
            get => _songTitle;
            set => SetProperty(ref _songTitle, value);
        }

        public string SongArtist
        {
            get => _songArtist;
            set => SetProperty(ref _songArtist, value);
        }

        public string Genre
        {
            get => _genre;
            set => SetProperty(ref _genre, value);
        }

        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        public string Mood
        {
            get => _mood;
            set => SetProperty(ref _mood, value);
        }

        public string ServerCached
        {
            get => _serverCached;
            set => SetProperty(ref _serverCached, value);
        }

        public string MatureContent
        {
            get => _matureContent;
            set => SetProperty(ref _matureContent, value);
        }

        public string GainValue
        {
            get => _gainValue;
            set => SetProperty(ref _gainValue, value);
        }

        public string FadeOutStart
        {
            get => _fadeOutStart;
            set => SetProperty(ref _fadeOutStart, value);
        }

        public string IntroMute
        {
            get => _introMute;
            set => SetProperty(ref _introMute, value);
        }

        public string SongUrl
        {
            get => _songUrl;
            set => SetProperty(ref _songUrl, value);
        }

        public ICommand CloseCommand { get; }
        public ICommand CopySongUrlCommand { get; }

        public SongDetailsViewModel(IUserSessionService userSessionService, SettingsService? settingsService = null)
        {
            _userSessionService = userSessionService;
            _settingsService = settingsService ?? SettingsService.Instance;
            CloseCommand = new RelayCommand(ExecuteCloseCommand);
            CopySongUrlCommand = new RelayCommand(ExecuteCopySongUrlCommand);
        }

        public async Task LoadSongDetailsAsync(int songId)
        {
            try
            {
                ResetToUnknown();
                Log.Information("[SONGDETAILSVIEWMODEL] Loading song details for SongId={SongId}", songId);
                if (string.IsNullOrWhiteSpace(_userSessionService.Token))
                {
                    SetErrorState("Authentication token is missing. Cannot load song details.");
                    return;
                }

                var apiUrl = _settingsService.Settings.ApiUrl;
                if (string.IsNullOrWhiteSpace(apiUrl) || !Uri.TryCreate(apiUrl, UriKind.Absolute, out var baseUri))
                {
                    SetErrorState($"Configured API URL '{apiUrl}' is invalid.");
                    return;
                }

                using var client = new HttpClient
                {
                    BaseAddress = baseUri,
                    Timeout = TimeSpan.FromSeconds(10)
                };

                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userSessionService.Token);
                var endpoint = $"/api/songs/{songId}";
                Log.Debug("[SONGDETAILSVIEWMODEL] Requesting song details from {Endpoint}", new Uri(client.BaseAddress!, endpoint));
                var response = await client.GetAsync(endpoint);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    Log.Debug("[SONGDETAILSVIEWMODEL] API response for SongId={SongId}: {Json}", songId, json);
                    var song = JsonSerializer.Deserialize<SongDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (song != null)
                    {
                        SongId = song.Id.ToString();
                        SongTitle = song.Title ?? SongTitle;
                        SongArtist = song.Artist ?? SongArtist;
                        Genre = song.Genre ?? "N/A";
                        Status = song.Status ?? "N/A";
                        Mood = song.Mood ?? "N/A";
                        ServerCached = song.Cached ? "Yes" : "No";
                        MatureContent = song.Mature ? "Yes" : "No";
                        GainValue = song.NormalizationGain.HasValue ? $"{song.NormalizationGain.Value:+0.00;-0.00;0.00} dB" : "0.00 dB";
                        FadeOutStart = FormatSeconds(song.FadeStartTime);
                        IntroMute = FormatSeconds(song.IntroMuteDuration);
                        SongUrl = string.IsNullOrWhiteSpace(song.YouTubeUrl) ? "N/A" : song.YouTubeUrl!;
                        Log.Information("[SONGDETAILSVIEWMODEL] Loaded song details: SongId={SongId}, Title={Title}, Artist={Artist}", SongId, SongTitle, SongArtist);
                    }
                    else
                    {
                        Log.Warning("[SONGDETAILSVIEWMODEL] Deserialization failed for SongId={SongId}", songId);
                        SetErrorState("Failed to deserialize song details.");
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Log.Warning("[SONGDETAILSVIEWMODEL] API request failed for SongId={SongId}, StatusCode={StatusCode}, Error={Error}", songId, response.StatusCode, errorContent);
                    SetErrorState($"API returned {response.StatusCode}");
                }
            }
            catch (TaskCanceledException ex)
            {
                Log.Error("[SONGDETAILSVIEWMODEL] Timed out loading song details for SongId={SongId}: {Message}", songId, ex.Message);
                SetErrorState("Song detail request timed out.");
            }
            catch (Exception ex)
            {
                Log.Error("[SONGDETAILSVIEWMODEL] Failed to load song details for SongId={SongId}: {Message}", songId, ex.Message);
                SetErrorState(ex.Message);
            }
        }

        private void ExecuteCloseCommand(object? parameter)
        {
            try
            {
                if (parameter is Window window)
                {
                    window.Close();
                    Log.Information("[SONGDETAILSVIEWMODEL] Closed SongDetailsWindow");
                }
                else
                {
                    Log.Warning("[SONGDETAILSVIEWMODEL] CloseCommand parameter is not a Window: {ParameterType}", parameter?.GetType().Name ?? "null");
                }
            }
            catch (Exception ex)
            {
                Log.Error("[SONGDETAILSVIEWMODEL] Failed to close SongDetailsWindow: {Message}", ex.Message);
            }
        }

        private void ExecuteCopySongUrlCommand(object? parameter)
        {
            try
            {
                if (!string.IsNullOrEmpty(SongUrl) && SongUrl != "N/A")
                {
                    Clipboard.SetText(SongUrl);
                    Log.Information("[SONGDETAILSVIEWMODEL] Copied SongUrl to clipboard: {SongUrl}", SongUrl);
                }
                else
                {
                    Log.Warning("[SONGDETAILSVIEWMODEL] Cannot copy SongUrl to clipboard: URL is empty or invalid");
                }
            }
            catch (Exception ex)
            {
                Log.Error("[SONGDETAILSVIEWMODEL] Failed to copy SongUrl to clipboard: {Message}", ex.Message);
            }
        }

        private void ResetToUnknown()
        {
            Genre = SelectedQueueEntry?.Genre ?? "N/A";
            Status = SelectedQueueEntry?.Status ?? "N/A";
            Mood = "N/A";
            ServerCached = SelectedQueueEntry == null ? "N/A" : (SelectedQueueEntry.IsServerCached ? "Yes" : "No");
            MatureContent = SelectedQueueEntry == null ? "N/A" : (SelectedQueueEntry.IsMature ? "Yes" : "No");
            GainValue = SelectedQueueEntry?.NormalizationGain.HasValue == true
                ? $"{SelectedQueueEntry.NormalizationGain.Value:+0.00;-0.00;0.00} dB"
                : "0.00 dB";
            FadeOutStart = FormatSeconds(SelectedQueueEntry?.FadeStartTime);
            IntroMute = FormatSeconds(SelectedQueueEntry?.IntroMuteDuration);
            SongUrl = string.IsNullOrWhiteSpace(SelectedQueueEntry?.YouTubeUrl) ? "N/A" : SelectedQueueEntry.YouTubeUrl!;
        }

        private void SetErrorState(string reason)
        {
            Log.Warning("[SONGDETAILSVIEWMODEL] Falling back to cached song details for SongId={SongId}: {Reason}", SongId, reason);
            ResetToUnknown();
        }

        private static string FormatSeconds(float? seconds)
        {
            if (!seconds.HasValue || seconds.Value <= 0)
            {
                return "--:--";
            }

            return TimeSpan.FromSeconds(seconds.Value).ToString(@"m\:ss");
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

    public class SongDto
    {
        public int Id { get; set; }
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public string? Genre { get; set; }
        public string? Decade { get; set; }
        public string? YouTubeUrl { get; set; }
        public string? Status { get; set; }
        public string? RequestedBy { get; set; }
        public DateTime? RequestDate { get; set; }
        public string? ApprovedBy { get; set; }
        public string? SpotifyId { get; set; }
        public string? MusicBrainzId { get; set; }
        public int? Popularity { get; set; }
        public int? LastFmPlaycount { get; set; }
        public float? Danceability { get; set; }
        public float? Energy { get; set; }
        public float? Valence { get; set; }
        public float? Bpm { get; set; }
        public string? Mood { get; set; }
        public bool Cached { get; set; }
        public bool Mature { get; set; }
        public float? NormalizationGain { get; set; }
        public float? FadeStartTime { get; set; }
        public float? IntroMuteDuration { get; set; }
    }
}
