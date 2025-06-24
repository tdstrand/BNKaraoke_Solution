using BNKaraoke.DJ.Models;
using BNKaraoke.DJ.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace BNKaraoke.DJ.ViewModels
{
    public partial class SungSongsViewModel : ObservableObject
    {
        private readonly IApiService _apiService;
        private readonly string _eventId;

        [ObservableProperty]
        private ObservableCollection<QueueEntry> _sungSongs = new();

        [ObservableProperty]
        private int _sungCount;

        public IRelayCommand CloseCommand { get; }

        public SungSongsViewModel(IApiService apiService, string eventId)
        {
            _apiService = apiService;
            _eventId = eventId;
            CloseCommand = new RelayCommand(ExecuteClose);
            Log.Information("[SUNGSONGSVIEWMODEL] Initialized for EventId={EventId}", _eventId);
        }

        public async Task InitializeAsync()
        {
            try
            {
                Log.Information("[SUNGSONGSVIEWMODEL] Starting InitializeAsync for EventId={EventId}", _eventId);
                var sungSongs = await _apiService.GetSungQueueAsync(_eventId);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    SungSongs.Clear();
                    foreach (var song in sungSongs.OrderBy(s => s.SungAt ?? DateTime.MaxValue))
                    {
                        SungSongs.Add(song);
                    }
                    SungCount = SungSongs.Count;
                    Log.Information("[SUNGSONGSVIEWMODEL] Loaded {Count} sung songs for EventId={EventId}", SungCount, _eventId);
                    OnPropertyChanged(nameof(SungSongs));
                    OnPropertyChanged(nameof(SungCount));
                });
            }
            catch (Exception ex)
            {
                Log.Error("[SUNGSONGSVIEWMODEL] Failed to load sung songs for EventId={EventId}: {Message}", _eventId, ex.Message);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    SungCount = 0;
                    OnPropertyChanged(nameof(SungCount));
                });
            }
        }

        private void ExecuteClose(object? parameter)
        {
            try
            {
                if (parameter is Window window)
                {
                    window.Close();
                    Log.Information("[SUNGSONGSVIEWMODEL] Closed SungSongsView");
                }
                else
                {
                    Log.Warning("[SUNGSONGSVIEWMODEL] CloseCommand parameter is not a Window: {ParameterType}", parameter?.GetType().Name ?? "null");
                }
            }
            catch (Exception ex)
            {
                Log.Error("[SUNGSONGSVIEWMODEL] Failed to close SungSongsView: {Message}", ex.Message);
            }
        }

        private class RelayCommand : IRelayCommand
        {
            private readonly Action<object?> _execute;
            private readonly Predicate<object?>? _canExecute;

            public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
            {
                _execute = execute;
                _canExecute = canExecute;
            }

            public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

            public void Execute(object? parameter) => _execute(parameter);

            public event EventHandler? CanExecuteChanged
            {
                add => CommandManager.RequerySuggested += value;
                remove => CommandManager.RequerySuggested -= value;
            }

            public void NotifyCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
        }
    }
}