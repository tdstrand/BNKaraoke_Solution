﻿using BNKaraoke.DJ.Models;
using BNKaraoke.DJ.Views;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Timer = System.Timers.Timer;

namespace BNKaraoke.DJ.ViewModels
{
    public partial class DJScreenViewModel
    {
        private readonly SemaphoreSlim _queueUpdateSemaphore = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _queueUpdateCts = new CancellationTokenSource();

        private async Task UpdateQueueColorsAndRules()
        {
            if (string.IsNullOrEmpty(_currentEventId) || QueueEntries == null) return;

            await _queueUpdateSemaphore.WaitAsync();
            try
            {
                var cts = Interlocked.Exchange(ref _queueUpdateCts, new CancellationTokenSource());
                cts.Cancel();
                cts.Dispose();
                var token = _queueUpdateCts.Token;

                Log.Information("[DJSCREEN QUEUE] Updating queue colors and rules for EventId={EventId}", _currentEventId);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    token.ThrowIfCancellationRequested();

                    foreach (var entry in QueueEntries.Where(e => e.IsUpNext))
                    {
                        Log.Information("[DJSCREEN QUEUE] Clearing IsUpNext for QueueId={QueueId}, SongTitle={SongTitle}", entry.QueueId, entry.SongTitle);
                        entry.IsUpNext = false;
                    }

                    foreach (var entry in QueueEntries.OrderBy(e => e.Position))
                    {
                        Log.Information("[DJSCREEN QUEUE] Evaluating QueueId={QueueId}, Position={Position}, SongTitle={SongTitle}, RequestorUserName={RequestorUserName}, IsOnHold={IsOnHold}, SingerStatus=IsSingerLoggedIn:{IsSingerLoggedIn}, IsSingerJoined:{IsSingerJoined}, IsSingerOnBreak:{IsSingerOnBreak}",
                            entry.QueueId, entry.Position, entry.SongTitle, entry.RequestorUserName, entry.IsOnHold,
                            entry.IsSingerLoggedIn, entry.IsSingerJoined, entry.IsSingerOnBreak);
                    }

                    var nextEntry = QueueEntries
                        .Where(e => e.IsActive)
                        .OrderBy(e => e.Position)
                        .FirstOrDefault(e => !e.IsOnHold && e.IsSingerLoggedIn && e.IsSingerJoined && !e.IsSingerOnBreak);

                    if (nextEntry != null)
                    {
                        nextEntry.IsUpNext = true;
                        Log.Information("[DJSCREEN QUEUE] Set IsUpNext=True for QueueId={QueueId}, RequestorUserName={RequestorUserName}, SongTitle={SongTitle}, Position={Position}, IsSingerOnBreak={IsSingerOnBreak}",
                            nextEntry.QueueId, nextEntry.RequestorUserName, nextEntry.SongTitle, nextEntry.Position, nextEntry.IsSingerOnBreak);
                    }
                    else
                    {
                        Log.Information("[DJSCREEN QUEUE] No eligible green singer found for IsUpNext in EventId={EventId}", _currentEventId);
                    }

                    OnPropertyChanged(nameof(QueueEntries));
                }, DispatcherPriority.Background, token);
            }
            catch (OperationCanceledException)
            {
                Log.Information("[DJSCREEN QUEUE] Queue update cancelled for EventId={EventId}", _currentEventId);
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN QUEUE] Failed to update queue colors and rules for EventId={EventId}: {Message}", _currentEventId, ex.Message);
                SetWarningMessage($"Failed to update queue: {ex.Message}");
            }
            finally
            {
                _queueUpdateSemaphore.Release();
            }
        }

        [RelayCommand]
        private async Task ShowSongDetails()
        {
            Log.Information("[DJSCREEN] ShowSongDetails command invoked");
            if (SelectedQueueEntry != null)
            {
                try
                {
                    var viewModel = new SongDetailsViewModel(_userSessionService)
                    {
                        SelectedQueueEntry = SelectedQueueEntry
                    };
                    await viewModel.LoadSongDetailsAsync(SelectedQueueEntry.SongId);
                    var songDetailsWindow = new SongDetailsWindow
                    {
                        WindowStartupLocation = WindowStartupLocation.CenterScreen,
                        DataContext = viewModel
                    };
                    songDetailsWindow.ShowDialog();
                    Log.Information("[DJSCREEN] SongDetailsWindow closed");
                }
                catch (Exception ex)
                {
                    Log.Error("[DJSCREEN] Failed to show SongDetailsWindow: {Message}", ex.Message);
                    SetWarningMessage($"Failed to show song details: {ex.Message}");
                }
            }
            else
            {
                Log.Information("[DJSCREEN] No queue entry selected for song details");
                SetWarningMessage("Please select a song to view details.");
            }
        }

        [RelayCommand]
        private void StartDrag(object parameter)
        {
            try
            {
                var draggedItem = parameter as QueueEntry;
                Log.Information("[DJSCREEN] StartDrag command invoked for QueueId={QueueId}", draggedItem?.QueueId ?? -1);
                if (draggedItem == null)
                {
                    Log.Error("[DJSCREEN] Drag failed: Dragged item is null");
                    SetWarningMessage("Drag failed: No item selected.");
                    return;
                }

                var listView = Application.Current.Windows.OfType<DJScreen>()
                    .Select(w => w.FindName("QueueListView") as ListView)
                    .FirstOrDefault(lv => lv != null);

                if (listView == null)
                {
                    Log.Error("[DJSCREEN] Drag failed: QueueListView not found");
                    SetWarningMessage("Drag failed: Queue not found.");
                    return;
                }

                Log.Information("[DJSCREEN] Initiating DragDrop for queue {QueueId}", draggedItem.QueueId);
                var data = new DataObject(typeof(QueueEntry), draggedItem);
                DragDrop.DoDragDrop(listView, data, DragDropEffects.Move);
                Log.Information("[DJSCREEN] Completed drag for queue {QueueId}", draggedItem.QueueId);
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Drag failed: {Message}", ex.Message);
                SetWarningMessage($"Failed to drag: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task Drop(System.Windows.DragEventArgs e)
        {
            try
            {
                Log.Information("[DJSCREEN] Drop command invoked: SourceType={SourceType}, Handled={Handled}", e?.Source?.GetType().Name ?? "null", e?.Handled ?? false);
                if (string.IsNullOrEmpty(_currentEventId))
                {
                    Log.Warning("[DJSCREEN] Drop failed: No event joined");
                    SetWarningMessage("Drop failed: No event joined.");
                    return;
                }

                if (e == null)
                {
                    Log.Error("[DJSCREEN] Drop failed: DragEventArgs is null");
                    SetWarningMessage("Drop failed: Invalid drag data.");
                    return;
                }

                Log.Information("[DJSCREEN] Accessing dragged data");
                var draggedItem = e.Data.GetData(typeof(QueueEntry)) as QueueEntry;
                if (draggedItem == null)
                {
                    Log.Warning("[DJSCREEN] Drop failed: Dragged item is null or not a QueueEntry");
                    SetWarningMessage("Drop failed: Invalid dragged item.");
                    return;
                }

                Log.Information("[DJSCREEN] Accessing target element");
                var target = e.OriginalSource as FrameworkElement;
                var listViewItem = target;
                while (listViewItem != null && !(listViewItem is ListViewItem))
                {
                    listViewItem = VisualTreeHelper.GetParent(listViewItem) as FrameworkElement;
                }
                var targetItem = (listViewItem as ListViewItem)?.DataContext as QueueEntry;

                if (targetItem == null)
                {
                    Log.Warning("[DJSCREEN] Drop failed: Target item is null or not a QueueEntry, OriginalSourceType={OriginalSourceType}", e.OriginalSource?.GetType().Name);
                    SetWarningMessage("Drop failed: Invalid target item.");
                    return;
                }

                if (draggedItem == targetItem)
                {
                    Log.Information("[DJSCREEN] Drop ignored: Dragged item is the same as target");
                    return;
                }

                if (IsPlaying && PlayingQueueEntry != null &&
                    (draggedItem.QueueId == PlayingQueueEntry.QueueId || targetItem.QueueId == PlayingQueueEntry.QueueId))
                {
                    Log.Information("[DJSCREEN] Drop failed: Cannot reorder playing song, QueueId={QueueId}", draggedItem.QueueId);
                    SetWarningMessage("Cannot reorder the playing song.");
                    return;
                }

                Log.Information("[DJSCREEN] Calculating indices for queue {QueueId}", draggedItem.QueueId);
                int sourceIndex = QueueEntries.IndexOf(draggedItem);
                int targetIndex = QueueEntries.IndexOf(targetItem);

                if (sourceIndex < 0 || targetIndex < 0)
                {
                    Log.Warning("[DJSCREEN] Drop failed: Invalid source or target index, SourceIndex={SourceIndex}, TargetIndex={TargetIndex}", sourceIndex, targetIndex);
                    SetWarningMessage("Drop failed: Invalid queue indices.");
                    return;
                }

                Log.Information("[DJSCREEN] Reordering queue locally");
                Application.Current.Dispatcher.Invoke(() =>
                {
                    QueueEntries.Move(sourceIndex, targetIndex);
                    for (int i = 0; i < QueueEntries.Count; i++)
                    {
                        QueueEntries[i].Position = i + 1;
                    }
                    OnPropertyChanged(nameof(QueueEntries));
                });

                var queueIds = QueueEntries.Select(q => q.QueueId.ToString()).ToList();
                Log.Information("[DJSCREEN] Reorder payload: EventId={EventId}, QueueIds={QueueIds}", _currentEventId, string.Join(",", queueIds));

                try
                {
                    await _apiService.ReorderQueueAsync(_currentEventId!, queueIds);
                    Log.Information("[DJSCREEN] Queue reordered for event {EventId}, dropped {SourceQueueId} to position {TargetIndex}",
                        _currentEventId, draggedItem.QueueId, targetIndex + 1);
                    await LoadQueueData();
                    await UpdateQueueColorsAndRules();
                    await LoadSungCountAsync();
                    Log.Information("[DJSCREEN] Refreshed queue data after reorder for event {EventId}", _currentEventId);
                }
                catch (Exception ex)
                {
                    Log.Error("[DJSCREEN] Failed to persist queue order: {Message}", ex.Message);
                    SetWarningMessage($"Failed to reorder queue: {ex.Message}");
                    await LoadQueueData();
                }
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Drop failed: {Message}", ex.Message);
                SetWarningMessage($"Failed to reorder queue: {ex.Message}");
            }
        }

        [RelayCommand]
        private void ToggleAutoPlay()
        {
            Log.Information("[DJSCREEN] ToggleAutoPlay command invoked");
            if (_isDisposing) return;
            IsAutoPlayEnabled = !IsAutoPlayEnabled;
            AutoPlayButtonText = IsAutoPlayEnabled ? "Auto Play: ON" : "Auto Play: OFF";
            Log.Information("[DJSCREEN] AutoPlay set to: {State}", IsAutoPlayEnabled);
        }

        [RelayCommand]
        private async Task Skip()
        {
            Log.Information("[DJSCREEN] Skip command invoked");
            if (_isDisposing) return;

            var targetEntry = PlayingQueueEntry ?? SelectedQueueEntry;
            if (targetEntry == null || string.IsNullOrEmpty(_currentEventId))
            {
                Log.Information("[DJSCREEN] Skip failed: No queue entry playing/selected or no event joined, PlayingQueueEntry={Playing}, SelectedQueueEntry={Selected}, EventId={EventId}",
                    PlayingQueueEntry?.QueueId ?? -1, SelectedQueueEntry?.QueueId ?? -1, _currentEventId ?? "null");
                SetWarningMessage("Please select a song and join an event.");
                if (_videoPlayerWindow != null)
                {
                    _videoPlayerWindow.StopVideo();
                    Log.Information("[DJSCREEN] Video playback stopped due to no valid queue entry");
                    IsPlaying = false;
                    IsVideoPaused = false;
                    SliderPosition = 0;
                    CurrentVideoPosition = "--:--";
                    TimeRemainingSeconds = 0;
                    TimeRemaining = "0:00";
                    OnPropertyChanged(nameof(SliderPosition));
                    OnPropertyChanged(nameof(CurrentVideoPosition));
                    OnPropertyChanged(nameof(TimeRemaining));
                    OnPropertyChanged(nameof(TimeRemainingSeconds));
                    if (_updateTimer != null)
                    {
                        _updateTimer.Stop();
                        Log.Information("[DJSCREEN] Stopped update timer due to no valid queue entry");
                    }
                }
                return;
            }

            try
            {
                if (_videoPlayerWindow != null)
                {
                    _videoPlayerWindow.StopVideo();
                    Log.Information("[DJSCREEN] Video playback stopped for QueueId={QueueId}", targetEntry.QueueId);
                }
                targetEntry.WasSkipped = true;
                IsPlaying = false;
                IsVideoPaused = false;
                SliderPosition = 0;
                CurrentVideoPosition = "--:--";
                TimeRemainingSeconds = 0;
                TimeRemaining = "0:00";
                OnPropertyChanged(nameof(SliderPosition));
                OnPropertyChanged(nameof(CurrentVideoPosition));
                OnPropertyChanged(nameof(TimeRemaining));
                OnPropertyChanged(nameof(TimeRemainingSeconds));
                if (_updateTimer != null)
                {
                    _updateTimer.Stop();
                    Log.Information("[DJSCREEN] Stopped update timer for QueueId={QueueId}", targetEntry.QueueId);
                }

                await _apiService.CompleteSongAsync(_currentEventId!, targetEntry.QueueId);
                Log.Information("[DJSCREEN] Skip request sent for event {EventId}, queue {QueueId}: {SongTitle}", _currentEventId, targetEntry.QueueId, targetEntry.SongTitle);

                if (PlayingQueueEntry != null)
                {
                    var entry = QueueEntries.FirstOrDefault(q => q.QueueId == PlayingQueueEntry.QueueId);
                    if (entry != null)
                    {
                        int sourceIndex = QueueEntries.IndexOf(entry);
                        if (sourceIndex >= 0)
                        {
                            QueueEntries.Move(sourceIndex, QueueEntries.Count - 1);
                            for (int i = 0; i < QueueEntries.Count; i++)
                            {
                                QueueEntries[i].Position = i + 1;
                            }
                            var queueIds = QueueEntries.Select(q => q.QueueId.ToString()).ToList();
                            Log.Information("[DJSCREEN] Reordering queue for event {EventId}, QueueIds={QueueIds}", _currentEventId, string.Join(",", queueIds));
                            try
                            {
                                await _apiService.ReorderQueueAsync(_currentEventId!, queueIds);
                                Log.Information("[DJSCREEN] Moved skipped song to end: QueueId={QueueId}", entry.QueueId);
                            }
                            catch (Exception reorderEx)
                            {
                                Log.Error("[DJSCREEN] Failed to reorder queue for QueueId={QueueId}: {Message}", entry.QueueId, reorderEx.Message);
                            }
                        }
                    }
                    TotalSongsPlayed++;
                    SungCount++;
                    OnPropertyChanged(nameof(TotalSongsPlayed));
                    OnPropertyChanged(nameof(SungCount));
                    Log.Information("[DJSCREEN] Incremented TotalSongsPlayed: {Count}, SungCount: {SungCount}", TotalSongsPlayed, SungCount);
                    QueueEntries.Remove(PlayingQueueEntry);
                    PlayingQueueEntry = null;
                    OnPropertyChanged(nameof(PlayingQueueEntry));
                    OnPropertyChanged(nameof(QueueEntries));
                }
                await LoadQueueData();
                await UpdateQueueColorsAndRules();
                await LoadSungCountAsync();
            }
            catch (HttpRequestException ex)
            {
                Log.Error("[DJSCREEN] Failed to skip queue {QueueId}: StatusCode={StatusCode}, Message={Message}", targetEntry.QueueId, ex.StatusCode, ex.Message);
                SetWarningMessage($"Failed to skip: {ex.Message}");
                if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    await LoadQueueData();
                }
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to skip queue {QueueId}: {Message}", targetEntry.QueueId, ex.Message);
                SetWarningMessage($"Failed to skip: {ex.Message}");
            }
        }

        public async Task LoadQueueData()
        {
            if (string.IsNullOrEmpty(_currentEventId)) return;
            try
            {
                Log.Information("[DJSCREEN] Loading queue data for event: {EventId}", _currentEventId);
                var queueEntries = await _apiService.GetQueueAsync(_currentEventId);
                Log.Information("[DJSCREEN] API returned {Count} queue entries for event {EventId}, QueueIds={QueueIds}",
                    queueEntries.Count, _currentEventId, string.Join(",", queueEntries.Select(q => q.QueueId)));

                var expectedQueueIds = new[] { 1000, 1001, 1002, 1003, 1005, 1006, 1009, 1010, 1011, 1012, 1013, 1014, 1016, 1017, 1020, 1021, 1022, 1023, 1024, 1025, 1027, 1028, 1031, 1032, 1033, 1034, 1035, 1036, 1038, 1039, 1042, 1043, 1044, 1045, 1046, 1047, 1049, 1050, 1053, 1054, 1055, 1056 };
                var missingQueueIds = expectedQueueIds.Except(queueEntries.Select(q => q.QueueId)).ToList();
                if (missingQueueIds.Any())
                {
                    Log.Information("[DJSCREEN] Archived or missing QueueIds: {MissingQueueIds}", string.Join(",", missingQueueIds));
                }

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    QueueEntries.Clear();
                    foreach (var entry in queueEntries.OrderBy(q => q.Position))
                    {
                        var matchingSinger = Singers.FirstOrDefault(s => s.UserId == entry.RequestorUserName);
                        if (matchingSinger != null)
                        {
                            entry.IsSingerOnBreak = matchingSinger.IsOnBreak;
                            Log.Information("[DJSCREEN] Synced IsSingerOnBreak={IsSingerOnBreak} for QueueId={QueueId}, RequestorUserName={RequestorUserName}, SingerDisplayName={SingerDisplayName}",
                                entry.IsSingerOnBreak, entry.QueueId, entry.RequestorUserName, matchingSinger.DisplayName);
                        }

                        entry.RequestorDisplayName = entry.RequestorDisplayName;
                        entry.IsSingerLoggedIn = entry.IsSingerLoggedIn;
                        entry.IsSingerJoined = entry.IsSingerJoined;
                        if (entry.Singers != null && entry.Singers.Any())
                        {
                            entry.Singers = entry.Singers.ToList();
                        }
                        if (_videoCacheService != null)
                        {
                            entry.IsVideoCached = _videoCacheService.IsVideoCached(entry.SongId);
                            if (entry.IsVideoCached)
                            {
                                string videoPath = Path.Combine(_settingsService.Settings.VideoCachePath, $"{entry.SongId}.mp4");
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        using var libVLC = new LibVLC();
                                        using var media = new Media(libVLC, new Uri(videoPath));
                                        await media.Parse();
                                        await Application.Current.Dispatcher.InvokeAsync(() =>
                                        {
                                            entry.VideoLength = TimeSpan.FromMilliseconds(media.Duration).ToString(@"m\:ss");
                                            Log.Information("[DJSCREEN] Set VideoLength for SongId={SongId}: {VideoLength}", entry.SongId, entry.VideoLength);
                                        });
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.Error("[DJSCREEN] Failed to get duration for SongId={SongId}: {Message}", entry.SongId, ex.Message);
                                        Application.Current.Dispatcher.Invoke(() => entry.VideoLength = "");
                                    }
                                });
                            }
                            Log.Information("[DJSCREEN] Queue entry: SongId={SongId}, IsCached={IsCached}, CachePath={CachePath}, SongTitle={SongTitle}, RequestorDisplayName={RequestorDisplayName}, Singers={Singers}, VideoLength={VideoLength}, IsUpNext={IsUpNext}, IsOnHold={IsOnHold}, HoldReason={HoldReason}, IsSingerLoggedIn={IsSingerLoggedIn}, IsSingerJoined={IsSingerJoined}, IsSingerOnBreak={IsSingerOnBreak}",
                                entry.SongId, entry.IsVideoCached, Path.Combine(_settingsService.Settings.VideoCachePath, $"{entry.SongId}.mp4"),
                                entry.SongTitle ?? "null", entry.RequestorDisplayName ?? "null",
                                entry.Singers != null ? string.Join(",", entry.Singers) : "null",
                                entry.VideoLength, entry.IsUpNext, entry.IsOnHold, entry.HoldReason ?? "null",
                                entry.IsSingerLoggedIn, entry.IsSingerJoined, entry.IsSingerOnBreak);
                        }
                        QueueEntries.Add(entry);
                    }
                    if (SelectedQueueEntry == null && QueueEntries.Any())
                    {
                        SelectedQueueEntry = QueueEntries.First();
                        OnPropertyChanged(nameof(SelectedQueueEntry));
                    }
                    OnPropertyChanged(nameof(QueueEntries));
                    Log.Information("[DJSCREEN] Loaded {Count} queue entries for event {EventId}", QueueEntries.Count, _currentEventId);
                });
                await UpdateQueueColorsAndRules();
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to load queue for EventId={EventId}: {Message}", _currentEventId, ex.Message);
                SetWarningMessage($"Failed to load queue: {ex.Message}");
            }
        }

        private void HandleInitialQueue(List<EventQueueDto> queue)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                QueueEntries.Clear();
                foreach (var dto in queue.OrderBy(q => q.Position))
                {
                    var entry = new QueueEntry
                    {
                        QueueId = dto.QueueId,
                        EventId = dto.EventId,
                        SongId = dto.SongId,
                        SongTitle = dto.SongTitle,
                        SongArtist = dto.SongArtist,
                        RequestorDisplayName = dto.RequestorFullName,
                        VideoLength = "", // Set from cache or API if needed
                        Position = dto.Position,
                        Status = dto.Status,
                        RequestorUserName = dto.RequestorUserName,
                        Singers = dto.Singers,
                        IsActive = dto.IsActive,
                        WasSkipped = dto.WasSkipped,
                        IsCurrentlyPlaying = dto.IsCurrentlyPlaying,
                        SungAt = dto.SungAt,
                        IsOnBreak = dto.IsOnBreak,
                        IsOnHold = !string.IsNullOrEmpty(dto.HoldReason),
                        IsUpNext = dto.IsUpNext,
                        HoldReason = dto.HoldReason,
                        IsSingerLoggedIn = dto.IsSingerLoggedIn,
                        IsSingerJoined = dto.IsSingerJoined,
                        IsSingerOnBreak = dto.IsSingerOnBreak
                    };
                    // entry.IsVideoCached = _videoCacheService?.IsVideoCached(entry.SongId) ?? false;
                    QueueEntries.Add(entry);
                }
                OnPropertyChanged(nameof(QueueEntries));
                _ = UpdateQueueColorsAndRules();
            });
        }
    }
}