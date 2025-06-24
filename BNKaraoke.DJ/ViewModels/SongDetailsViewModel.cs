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
        private QueueEntry? _selectedQueueEntry;
        private string? _genre;
        private string? _decade;
        private string? _youTubeUrl;

        public QueueEntry? SelectedQueueEntry
        {
            get => _selectedQueueEntry;
            set => SetProperty(ref _selectedQueueEntry, value);
        }

        public string? Genre
        {
            get => _genre;
            set => SetProperty(ref _genre, value);
        }

        public string? Decade
        {
            get => _decade;
            set => SetProperty(ref _decade, value);
        }

        public string? YouTubeUrl
        {
            get => _youTubeUrl;
            set => SetProperty(ref _youTubeUrl, value);
        }

        public ICommand CloseCommand { get; }
        public ICommand CopyYouTubeUrlCommand { get; }

        public SongDetailsViewModel(IUserSessionService userSessionService)
        {
            _userSessionService = userSessionService;
            CloseCommand = new RelayCommand(ExecuteCloseCommand);
            CopyYouTubeUrlCommand = new RelayCommand(ExecuteCopyYouTubeUrlCommand);
        }

        public async Task LoadSongDetailsAsync(int songId)
        {
            try
            {
                Log.Information("[SONGDETAILSVIEWMODEL] Loading song details for SongId={SongId}", songId);
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _userSessionService.Token);
                var response = await client.GetAsync($"http://localhost:7290/api/songs/{songId}");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    Log.Debug("[SONGDETAILSVIEWMODEL] API response for SongId={SongId}: {Json}", songId, json);
                    var song = JsonSerializer.Deserialize<SongDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (song != null)
                    {
                        Genre = song.Genre ?? "N/A";
                        Decade = song.Decade ?? "N/A";
                        YouTubeUrl = song.YouTubeUrl ?? "N/A";
                        Log.Information("[SONGDETAILSVIEWMODEL] Loaded song details: SongId={SongId}, Genre={Genre}, Decade={Decade}, YouTubeUrl={YouTubeUrl}",
                            songId, Genre, Decade, YouTubeUrl);
                    }
                    else
                    {
                        Log.Warning("[SONGDETAILSVIEWMODEL] Deserialization failed for SongId={SongId}", songId);
                        Genre = "N/A";
                        Decade = "N/A";
                        YouTubeUrl = "N/A";
                    }
                }
                else
                {
                    Log.Warning("[SONGDETAILSVIEWMODEL] API request failed for SongId={SongId}, StatusCode={StatusCode}", songId, response.StatusCode);
                    Genre = "N/A";
                    Decade = "N/A";
                    YouTubeUrl = "N/A";
                }
            }
            catch (Exception ex)
            {
                Log.Error("[SONGDETAILSVIEWMODEL] Failed to load song details for SongId={SongId}: {Message}", songId, ex.Message);
                Genre = "Error";
                Decade = "Error";
                YouTubeUrl = "Error";
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

        private void ExecuteCopyYouTubeUrlCommand(object? parameter)
        {
            try
            {
                if (!string.IsNullOrEmpty(YouTubeUrl) && YouTubeUrl != "N/A")
                {
                    Clipboard.SetText(YouTubeUrl);
                    Log.Information("[SONGDETAILSVIEWMODEL] Copied YouTubeUrl to clipboard: {YouTubeUrl}", YouTubeUrl);
                }
                else
                {
                    Log.Warning("[SONGDETAILSVIEWMODEL] Cannot copy YouTubeUrl to clipboard: URL is empty or invalid");
                }
            }
            catch (Exception ex)
            {
                Log.Error("[SONGDETAILSVIEWMODEL] Failed to copy YouTubeUrl to clipboard: {Message}", ex.Message);
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
    }
}