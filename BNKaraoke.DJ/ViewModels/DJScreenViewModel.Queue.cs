using BNKaraoke.DJ.Models;
using BNKaraoke.DJ.Views;
using BNKaraoke.DJ.Services;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
using System.Text.Json;
using Timer = System.Timers.Timer;

namespace BNKaraoke.DJ.ViewModels
{
    public partial class DJScreenViewModel
    {
        private readonly SemaphoreSlim _queueUpdateSemaphore = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _queueUpdateCts = new CancellationTokenSource();

        private void ClearQueueCollections()
        {
            foreach (var entry in _queueEntryLookup.Values)
            {
                entry.PropertyChanged -= QueueEntryTracking_PropertyChanged;
            }

            _queueEntryLookup.Clear();
            _hiddenQueueEntryIds.Clear();
            QueueEntries.Clear();
        }

        private QueueEntryViewModel? GetTrackedQueueEntry(int queueId)
        {
            return queueId > 0 && _queueEntryLookup.TryGetValue(queueId, out var entry)
                ? entry
                : null;
        }

        private void RegisterQueueEntry(QueueEntryViewModel entry)
        {
            if (entry == null)
            {
                return;
            }

            entry.PropertyChanged -= QueueEntryTracking_PropertyChanged;
            entry.PropertyChanged += QueueEntryTracking_PropertyChanged;
            _queueEntryLookup[entry.QueueId] = entry;
        }

        private void UnregisterQueueEntry(QueueEntryViewModel entry)
        {
            if (entry == null)
            {
                return;
            }

            entry.PropertyChanged -= QueueEntryTracking_PropertyChanged;

            if (_queueEntryLookup.TryGetValue(entry.QueueId, out var tracked) && ReferenceEquals(tracked, entry))
            {
                _queueEntryLookup.Remove(entry.QueueId);
            }

            _hiddenQueueEntryIds.Remove(entry.QueueId);
            QueueEntries.Remove(entry);
        }

        private void UpdateEntryVisibility(QueueEntryViewModel entry)
        {
            if (entry.IsPlayed)
            {
                if (QueueEntries.Contains(entry))
                {
                    QueueEntries.Remove(entry);
                }

                _hiddenQueueEntryIds.Add(entry.QueueId);
                return;
            }

            _hiddenQueueEntryIds.Remove(entry.QueueId);
            InsertQueueEntryOrdered(QueueEntries, entry);
        }

        private static void InsertQueueEntryOrdered(ObservableCollection<QueueEntryViewModel> collection, QueueEntryViewModel entry)
        {
            if (collection.Contains(entry))
            {
                collection.Remove(entry);
            }

            var index = 0;
            while (index < collection.Count && collection[index].Position <= entry.Position)
            {
                index++;
            }

            collection.Insert(index, entry);
        }

        private void QueueEntryTracking_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not QueueEntryViewModel entry || e.PropertyName != nameof(QueueEntryViewModel.IsPlayed))
            {
                return;
            }

            if (Application.Current?.Dispatcher == null)
            {
                UpdateEntryVisibility(entry);
                return;
            }

            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                UpdateEntryVisibility(entry);
                LogQueueSummary("Updated");
                OnPropertyChanged(nameof(QueueEntries));
            }, DispatcherPriority.Background);
        }

        private void LogQueueSummary(string context)
        {
            var activeCount = QueueEntries.Count;
            var sungCount = _hiddenQueueEntryIds.Count;

            if (string.Equals(context, "Loaded", StringComparison.OrdinalIgnoreCase))
            {
                Log.Information("[DJSCREEN QUEUE] Loaded: {Active} active, {Sung} sung (hidden)", activeCount, sungCount);
            }
            else
            {
                Log.Information("[DJSCREEN QUEUE] {Context}: {Active} active, {Sung} sung (hidden)", context, activeCount, sungCount);
            }
        }

        private async Task UpdateQueueColorsAndRules()
        {
            if (string.IsNullOrEmpty(_currentEventId) || QueueEntries == null) return;

            await _queueUpdateSemaphore.WaitAsync().ConfigureAwait(false);
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

                    ApplyQueueVisualRules(token);

                    if (IsAutoPlayEnabled)
                    {
                        ApplyAutoplayRules(token);
                    }

                    var totalCount = QueueEntries.Count;
                    var heldCount = QueueEntries.Count(entry => entry.IsOnHold);
                    var readyCount = QueueEntries.Count(entry => entry.IsReady);
                    Log.Information("[DJSCREEN QUEUE] Rules applied: {Total} shown, {Held} on hold, {Ready} ready, Autoplay={Auto}",
                        totalCount,
                        heldCount,
                        readyCount,
                        IsAutoPlayEnabled);

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

        private void ApplyQueueVisualRules(CancellationToken token)
        {
            foreach (var entry in QueueEntries.OrderBy(e => e.Position))
            {
                token.ThrowIfCancellationRequested();

                Log.Information("[DJSCREEN QUEUE] Evaluating QueueId={QueueId}, Position={Position}, SongTitle={SongTitle}, RequestorUserName={RequestorUserName}, IsOnHold={IsOnHold}, SingerStatus=IsSingerLoggedIn:{IsSingerLoggedIn}, IsSingerJoined:{IsSingerJoined}, IsSingerOnBreak:{IsSingerOnBreak}",
                    entry.QueueId, entry.Position, entry.SongTitle, entry.RequestorUserName, entry.IsOnHold,
                    entry.IsSingerLoggedIn, entry.IsSingerJoined, entry.IsSingerOnBreak);
            }
        }

        private void ApplyAutoplayRules(CancellationToken token)
        {
            foreach (var entry in QueueEntries.Where(e => e.IsUpNext).ToList())
            {
                token.ThrowIfCancellationRequested();

                Log.Information("[DJSCREEN QUEUE] Clearing IsUpNext for QueueId={QueueId}, SongTitle={SongTitle}", entry.QueueId, entry.SongTitle);
                entry.IsUpNext = false;
            }

            var nextEntry = GetAutoplayCandidate();

            if (nextEntry != null)
            {
                token.ThrowIfCancellationRequested();

                nextEntry.IsUpNext = true;
                Log.Information("[DJSCREEN QUEUE] Set IsUpNext=True for QueueId={QueueId}, RequestorUserName={RequestorUserName}, SongTitle={SongTitle}, Position={Position}, IsSingerOnBreak={IsSingerOnBreak}",
                    nextEntry.QueueId, nextEntry.RequestorUserName, nextEntry.SongTitle, nextEntry.Position, nextEntry.IsSingerOnBreak);
            }
            else
            {
                Log.Information("[DJSCREEN QUEUE] No eligible green singer found for IsUpNext in EventId={EventId}", _currentEventId);
            }
        }

        private QueueEntryViewModel? GetAutoplayCandidate()
        {
            return QueueEntries
                .OrderBy(entry => entry.Position)
                .FirstOrDefault(entry => entry.IsReady);
        }

        [RelayCommand]
        private async Task ShowSongDetails()
        {
            Log.Information("[DJSCREEN] ShowSongDetails command invoked");
            if (SelectedQueueEntry != null)
            {
                try
                {
                    var viewModel = new SongDetailsViewModel(_userSessionService, _settingsService)
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
        private async Task OpenReorderModal()
        {
            try
            {
                if (string.IsNullOrEmpty(_currentEventId))
                {
                    SetWarningMessage("Join an event before reordering the queue.");
                    return;
                }

                if (QueueEntries == null || QueueEntries.Count == 0)
                {
                    SetWarningMessage("The queue is empty. Nothing to reorder.");
                    return;
                }

                var snapshot = QueueEntries
                    .OrderBy(entry => entry.Position)
                    .Select((entry, index) => ReorderQueuePreviewItem.FromQueueEntry(index, entry, index < 2))
                    .ToList();

                if (snapshot.Count == 0)
                {
                    SetWarningMessage("No reorderable songs were found.");
                    return;
                }

                if (!int.TryParse(_currentEventId, out var eventId))
                {
                    Log.Warning("[DJSCREEN QUEUE] Unable to parse EventId '{EventId}' for reorder preview", _currentEventId);
                    SetWarningMessage("Unable to open reorder dialog: invalid event identifier.");
                    return;
                }

                var modalViewModel = new ReorderQueueModalViewModel(_apiService, _settingsService, _userSessionService, eventId, snapshot);
                var modal = new ReorderQueueModal
                {
                    Owner = Application.Current.Windows.OfType<DJScreen>().FirstOrDefault(),
                    DataContext = modalViewModel
                };

                void Handler(object? sender, bool approved)
                {
                    modalViewModel.RequestClose -= Handler;

                    if (modal.IsLoaded)
                    {
                        modal.Close();
                        return;
                    }

                    RoutedEventHandler? loadedHandler = null;
                    loadedHandler = (_, _) =>
                    {
                        modal.Loaded -= loadedHandler;
                        modal.Close();
                    };

                    modal.Loaded += loadedHandler;
                }

                modalViewModel.RequestClose += Handler;
                modal.ShowDialog();
                modalViewModel.RequestClose -= Handler;

                if (modalViewModel.IsApproved)
                {
                    if (string.IsNullOrEmpty(_currentEventId))
                    {
                        Log.Warning("[DJSCREEN QUEUE] Unable to apply reorder plan because EventId is null after approval");
                        SetWarningMessage("Unable to apply reorder: event context unavailable.");
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(modalViewModel.PlanId))
                    {
                        Log.Warning("[DJSCREEN QUEUE] Reorder approval received without a plan identifier.");
                        SetWarningMessage("Reorder preview did not produce a valid plan.");
                        return;
                    }

                    var applyRequest = new ReorderApplyRequest
                    {
                        EventId = eventId,
                        PlanId = modalViewModel.PlanId!,
                        BasedOnVersion = modalViewModel.BasedOnVersion ?? string.Empty,
                        IdempotencyKey = modalViewModel.IdempotencyKey
                    };

                    Log.Information(
                        "[DJSCREEN QUEUE] Applying reorder plan. EventId={EventId}, PlanId={PlanId}, BasedOnVersion={BasedOnVersion}, IdempotencyKey={IdempotencyKey}",
                        eventId,
                        applyRequest.PlanId,
                        applyRequest.BasedOnVersion,
                        applyRequest.IdempotencyKey);

                    try
                    {
                        var response = await _apiService.ApplyQueueReorderAsync(applyRequest);
                        Log.Information("[DJSCREEN QUEUE] Reorder applied successfully. AppliedVersion={Version}, Moves={MoveCount}", response.AppliedVersion, response.MoveCount);

                        await LoadQueueData();
                        await UpdateQueueColorsAndRules();
                        await LoadSungCountAsync();

                        SetWarningMessage("Queue reordered successfully.");
                    }
                    catch (ApiRequestException apiEx) when (apiEx.StatusCode == System.Net.HttpStatusCode.Conflict)
                    {
                        Log.Warning(apiEx, "[DJSCREEN QUEUE] Reorder apply conflicted with current queue state: {Message}", apiEx.Message);
                        SetWarningMessage(string.IsNullOrWhiteSpace(apiEx.Message) ? "Queue changed—Re-preview?" : apiEx.Message);
                    }
                    catch (ApiRequestException apiEx) when (apiEx.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
                    {
                        Log.Warning(apiEx, "[DJSCREEN QUEUE] Reorder apply returned validation error: {Message}", apiEx.Message);
                        var warningMessage = string.IsNullOrWhiteSpace(apiEx.Message)
                            ? "Unable to apply reorder plan."
                            : apiEx.Message;
                        if (apiEx.Warnings.Any())
                        {
                            warningMessage += " " + string.Join(" ", apiEx.Warnings.Select(w => w.Message));
                        }
                        SetWarningMessage(warningMessage);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "[DJSCREEN QUEUE] Failed to apply reorder plan for EventId={EventId}, PlanId={PlanId}: {Message}", _currentEventId, modalViewModel.PlanId, ex.Message);
                        SetWarningMessage($"Failed to apply reorder plan: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[DJSCREEN QUEUE] Failed to open reorder modal: {Message}", ex.Message);
                SetWarningMessage($"Failed to open reorder dialog: {ex.Message}");
            }
        }

        [RelayCommand]
        private void StartDrag(object parameter)
        {
            try
            {
                var draggedItem = parameter as QueueEntryViewModel;
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
                var data = new DataObject(typeof(QueueEntryViewModel), draggedItem);
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
                var draggedItem = e.Data.GetData(typeof(QueueEntryViewModel)) as QueueEntryViewModel;
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
                var targetItem = (listViewItem as ListViewItem)?.DataContext as QueueEntryViewModel;

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

                var newOrder = QueueEntries
                    .Select((q, i) => new QueuePosition { QueueId = q.QueueId, Position = i + 1 })
                    .ToList();
                Log.Information("[DJSCREEN] Reorder payload: EventId={EventId}, Payload={Payload}",
                    _currentEventId, JsonSerializer.Serialize(newOrder));

                try
                {
                    await _apiService.ReorderQueueAsync(_currentEventId!, newOrder);
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
            _ = UpdateQueueColorsAndRules();
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
                    SongPosition = 0;
                    CurrentVideoPosition = "--:--";
                    TimeRemainingSeconds = 0;
                    TimeRemaining = "--:--";
                    OnPropertyChanged(nameof(SongPosition));
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
                SongPosition = 0;
                CurrentVideoPosition = "--:--";
                TimeRemainingSeconds = 0;
                TimeRemaining = "--:--";
                OnPropertyChanged(nameof(SongPosition));
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
                            var newOrder = QueueEntries
                                .Select((q, i) => new QueuePosition { QueueId = q.QueueId, Position = i + 1 })
                                .ToList();
                            Log.Information("[DJSCREEN] Reordering queue for event {EventId}, Payload={Payload}",
                                _currentEventId, JsonSerializer.Serialize(newOrder));
                            try
                            {
                                await _apiService.ReorderQueueAsync(_currentEventId!, newOrder);
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
                    UnregisterQueueEntry(PlayingQueueEntry);
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

        [RelayCommand]
        private async Task RemoveSelected()
        {
            Log.Information("[DJSCREEN] RemoveSelected command invoked");
            if (_isDisposing) return;

            var targetEntry = SelectedQueueEntry;
            if (targetEntry == null || string.IsNullOrEmpty(_currentEventId))
            {
                Log.Information("[DJSCREEN] RemoveSelected failed: No queue entry selected or no event joined, SelectedQueueEntry={Selected}, EventId={EventId}",
                    SelectedQueueEntry?.QueueId ?? -1, _currentEventId ?? "null");
                SetWarningMessage("Please select a song and join an event.");
                return;
            }

            if (PlayingQueueEntry != null && targetEntry.QueueId == PlayingQueueEntry.QueueId)
            {
                Log.Information("[DJSCREEN] RemoveSelected targeting playing entry, delegating to Skip");
                await Skip();
                return;
            }

            try
            {
                await _apiService.CompleteSongAsync(_currentEventId!, targetEntry.QueueId);
                Log.Information("[DJSCREEN] Removed queue entry for event {EventId}, queue {QueueId}: {SongTitle}", _currentEventId, targetEntry.QueueId, targetEntry.SongTitle);

                UnregisterQueueEntry(targetEntry);
                for (int i = 0; i < QueueEntries.Count; i++)
                {
                    QueueEntries[i].Position = i + 1;
                }
                OnPropertyChanged(nameof(QueueEntries));

                var newOrder = QueueEntries
                    .Select((q, i) => new QueuePosition { QueueId = q.QueueId, Position = i + 1 })
                    .ToList();
                try
                {
                    await _apiService.ReorderQueueAsync(_currentEventId!, newOrder);
                }
                catch (Exception ex)
                {
                    Log.Error("[DJSCREEN] Failed to reorder queue after removal: {Message}", ex.Message);
                }

                if (QueueEntries.Count <= 20)
                {
                    await UpdateQueueColorsAndRules();
                }
                else
                {
                    Log.Information("[DJSCREEN] Deferred UpdateQueueColorsAndRules due to large queue size: {Count}", QueueEntries.Count);
                    var queueUpdateTask = UpdateQueueColorsAndRules(); // suppress CS4014
                }
                await LoadSungCountAsync();
            }
            catch (HttpRequestException ex)
            {
                Log.Error("[DJSCREEN] Failed to remove queue {QueueId}: StatusCode={StatusCode}, Message={Message}", targetEntry.QueueId, ex.StatusCode, ex.Message);
                SetWarningMessage($"Failed to remove: {ex.Message}");
                if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    await LoadQueueData();
                }
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to remove queue {QueueId}: {Message}", targetEntry.QueueId, ex.Message);
                SetWarningMessage($"Failed to remove: {ex.Message}");
            }
        }

        public async Task LoadQueueData()
        {
            if (string.IsNullOrEmpty(_currentEventId)) return;
            try
            {
                Log.Information("[DJSCREEN] Loading queue data for event: {EventId}", _currentEventId);
                var queueDtos = await _apiService.GetQueueAsync(_currentEventId);
                Log.Information("[DJSCREEN] API returned {Count} queue entries for event {EventId}, QueueIds={QueueIds}",
                    queueDtos.Count, _currentEventId, string.Join(",", queueDtos.Select(q => q.QueueId)));

                var expectedQueueIds = new[] { 1000, 1001, 1002, 1003, 1005, 1006, 1009, 1010, 1011, 1012, 1013, 1014, 1016, 1017, 1020, 1021, 1022, 1023, 1024, 1025, 1027, 1028, 1031, 1032, 1033, 1034, 1035, 1036, 1038, 1039, 1042, 1043, 1044, 1045, 1046, 1047, 1049, 1050, 1053, 1054, 1055, 1056 };
                var missingQueueIds = expectedQueueIds.Except(queueDtos.Select(q => q.QueueId)).ToList();
                if (missingQueueIds.Any())
                {
                    Log.Information("[DJSCREEN] Archived or missing QueueIds: {MissingQueueIds}", string.Join(",", missingQueueIds));
                }

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _queueUpdateMetadata.Clear();
                    ClearQueueCollections();
                    foreach (var dto in queueDtos.OrderBy(q => q.Position))
                    {
                        var entry = new QueueEntryViewModel();
                        ApplyQueueDtoToEntry(entry, dto);

                        IEnumerable<string> singerIds = entry.Singers != null && entry.Singers.Any()
                            ? entry.Singers
                            : string.IsNullOrWhiteSpace(entry.RequestorUserName)
                                ? Array.Empty<string>()
                                : new[] { entry.RequestorUserName };
                        var matchingSinger = Singers.FirstOrDefault(s => singerIds.Contains(s.UserId));
                        if (matchingSinger != null)
                        {
                            entry.IsSingerLoggedIn = matchingSinger.IsLoggedIn;
                            entry.IsSingerJoined = matchingSinger.IsJoined;
                            entry.IsSingerOnBreak = matchingSinger.IsOnBreak;
                            Log.Information("[DJSCREEN] Synced singer status for QueueId={QueueId}, RequestorUserName={RequestorUserName}, SingerDisplayName={SingerDisplayName}, LoggedIn={LoggedIn}, Joined={Joined}, OnBreak={OnBreak}",
                                entry.QueueId, entry.RequestorUserName, matchingSinger.DisplayName,
                                entry.IsSingerLoggedIn, entry.IsSingerJoined, entry.IsSingerOnBreak);
                        }

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
                            else if (entry.IsServerCached)
                            {
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        await _videoCacheService.CacheVideoAsync(entry.SongId);
                                        bool cached = _videoCacheService.IsVideoCached(entry.SongId);
                                        await Application.Current.Dispatcher.InvokeAsync(() => entry.IsVideoCached = cached);
                                        if (cached)
                                        {
                                            string videoPath = Path.Combine(_settingsService.Settings.VideoCachePath, $"{entry.SongId}.mp4");
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
                                                Log.Error("[DJSCREEN] Failed to get duration for SongId={SongId} after caching: {Message}", entry.SongId, ex.Message);
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.Error("[DJSCREEN] Failed to cache video for SongId={SongId}: {Message}", entry.SongId, ex.Message);
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

                        RegisterQueueEntry(entry);
                        UpdateEntryVisibility(entry);
                    }

                    if (SelectedQueueEntry == null || !QueueEntries.Contains(SelectedQueueEntry))
                    {
                        SelectedQueueEntry = QueueEntries.FirstOrDefault();
                        if (SelectedQueueEntry != null)
                        {
                            OnPropertyChanged(nameof(SelectedQueueEntry));
                        }
                    }

                    OnPropertyChanged(nameof(QueueEntries));
                    LogQueueSummary("Loaded");
                    Log.Information("[DJSCREEN] Loaded {Count} queue entries for event {EventId}", QueueEntries.Count, _currentEventId);
                    SyncQueueSingerStatuses();
                });
                _initialQueueTcs?.TrySetResult(true);
                ScheduleQueueReevaluation();
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
                _queueUpdateMetadata.Clear();
                ClearQueueCollections();
                foreach (var dto in queue.OrderBy(q => q.Position))
                {
                    var entry = new QueueEntryViewModel();
                    ApplyQueueDtoToEntry(entry, dto);
                    RegisterQueueEntry(entry);
                    UpdateEntryVisibility(entry);
                }
                OnPropertyChanged(nameof(QueueEntries));
                LogQueueSummary("Loaded");
                SyncQueueSingerStatuses();
                _initialQueueTcs?.TrySetResult(true);
                ScheduleQueueReevaluation();
            });
        }
    }
}