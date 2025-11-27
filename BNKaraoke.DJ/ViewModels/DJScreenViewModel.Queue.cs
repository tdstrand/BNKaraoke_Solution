using BNKaraoke.DJ.Models;
using BNKaraoke.DJ.Views;
using BNKaraoke.DJ.Services;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Text.Json;
using System.Runtime.CompilerServices;
using Timer = System.Timers.Timer;

namespace BNKaraoke.DJ.ViewModels
{
    public partial class DJScreenViewModel
    {
        // Queue collections
        // _queueEntryLookup holds the canonical queue state for the current event.
        // QueueEntriesInternal tracks entries currently visible in the DJ UI.
        // _hiddenQueueEntryIds keeps track of entries hidden from the UI (e.g., sung, skipped).
        // Visibility decisions funnel through IsVisiblyQueued.
        private readonly Dictionary<int, QueueEntryViewModel> _queueEntryLookup = new();
        private readonly HashSet<int> _hiddenQueueEntryIds = new();
        private readonly ObservableCollection<QueueEntryViewModel> _queueEntriesInternal = new();
        private readonly ConcurrentDictionary<int, Task> _cacheCheckTasks = new();
        private readonly HashSet<int> _verifiedLocalCacheSongIds = new();
        private readonly object _localCacheLock = new();
        private bool _restFallbackApplied;
        private DateTime? _restFallbackAppliedUtc;
        public ObservableCollection<QueueEntryViewModel> QueueEntriesInternal => _queueEntriesInternal;

        private void ClearQueueCollections([CallerMemberName] string? caller = null)
        {
            if (_hasReceivedInitialQueue && caller != null && caller.Contains("JoinLiveEvent", StringComparison.OrdinalIgnoreCase))
            {
                Log.Warning("[DJSCREEN QUEUE] Ignoring ClearQueueCollections from {Caller} because hydration already provided initial queue.", caller);
                return;
            }

            var trackedCount = _queueEntryLookup.Count;
            var hiddenCount = _hiddenQueueEntryIds.Count;
            var visibleCount = QueueEntriesInternal.Count;

            Log.Information(
                "[DJSCREEN QUEUE] ClearQueueCollections invoked by {Caller}. EventId={EventId}, trackedBefore={TrackedCount}, hiddenBefore={HiddenCount}, visibleBefore={VisibleCount}",
                caller ?? "<unknown>",
                _currentEventId ?? "<none>",
                trackedCount,
                hiddenCount,
                visibleCount);

            foreach (var entry in _queueEntryLookup.Values)
            {
                entry.UpdateLinkedSingers(Array.Empty<Singer>());
                entry.PropertyChanged -= QueueEntryTracking_PropertyChanged;
            }

            _queueEntryLookup.Clear();
            _hiddenQueueEntryIds.Clear();
            QueueEntriesInternal.Clear();
            ResetSnapshotWindow();
        }

        private QueueEntryViewModel? GetTrackedQueueEntry(int queueId)
        {
            return queueId > 0 && _queueEntryLookup.TryGetValue(queueId, out var entry)
                ? entry
                : null;
        }

        private void ApplyDtoToQueueEntry(QueueEntryViewModel entry, DJQueueItemDto dto, string source)
        {
            if (entry == null || dto == null)
            {
                return;
            }

            Log.Information(
                "[DJSCREEN QUEUE] UpsertV2 QueueId={QueueId} SingerNull={SingerNull} Source={Source}",
                dto.QueueId,
                dto.Singer == null,
                source);

            entry.ApplyV2QueueItem(dto);
            UpsertSingerFromQueueDto(dto);
            EnsureLocalCacheCheck(entry);
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

            entry.UpdateLinkedSingers(Array.Empty<Singer>());
            entry.PropertyChanged -= QueueEntryTracking_PropertyChanged;

            if (_queueEntryLookup.TryGetValue(entry.QueueId, out var tracked) && ReferenceEquals(tracked, entry))
            {
                _queueEntryLookup.Remove(entry.QueueId);
            }

            _hiddenQueueEntryIds.Remove(entry.QueueId);
            QueueEntriesInternal.Remove(entry);
        }

        private bool IsVisiblyQueued(QueueEntryViewModel entry)
        {
            return entry != null && !entry.IsPlayed;
        }

        private void EnsureLocalCacheCheck(QueueEntryViewModel? entry)
        {
            if (_videoCacheService == null || entry == null)
            {
                return;
            }

            var songId = entry.SongId;
            if (songId <= 0)
            {
                return;
            }

            var needsDuration = string.IsNullOrWhiteSpace(entry.VideoLength);
            if (!needsDuration)
            {
                lock (_localCacheLock)
                {
                    if (_verifiedLocalCacheSongIds.Contains(songId))
                    {
                        return;
                    }
                }
            }

            if (_cacheCheckTasks.ContainsKey(songId))
            {
                return;
            }

            Log.Information("[DJSCREEN CACHE] Scheduling local cache check for SongId={SongId}, NeedsDuration={NeedsDuration}, Verified={Verified}", songId, needsDuration, _verifiedLocalCacheSongIds.Contains(songId));
            _cacheCheckTasks.GetOrAdd(songId, _ => Task.Run(() => CheckAndCacheVideoAsync(songId)));
        }

        private async Task CheckAndCacheVideoAsync(int songId)
        {
            try
            {
                if (_videoCacheService == null)
                {
                    return;
                }

                Log.Information("[DJSCREEN CACHE] Local cache check started for SongId={SongId}", songId);
                var cached = _videoCacheService.IsVideoCached(songId);
                TimeSpan? duration = null;
                if (!cached)
                {
                    await _videoCacheService.CacheVideoAsync(songId);
                    cached = _videoCacheService.IsVideoCached(songId);
                }

                if (cached)
                {
                    duration = await _videoCacheService.TryGetVideoDurationAsync(songId);
                }

                Log.Information("[DJSCREEN CACHE] Local cache check complete for SongId={SongId}, Cached={Cached}, DurationSeconds={Duration}", songId, cached, duration?.TotalSeconds);

                if (!cached)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() => MarkEntriesAsNotCached(songId));
                    return;
                }

                var localDuration = duration;
                await Application.Current.Dispatcher.InvokeAsync(() => MarkEntriesAsCached(songId, localDuration));
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN CACHE] Failed local cache sync for SongId={SongId}: {Message}", songId, ex.Message);
            }
            finally
            {
                _cacheCheckTasks.TryRemove(songId, out _);
            }
        }

        private void MarkEntriesAsCached(int songId, TimeSpan? duration)
        {
            foreach (var entry in _queueEntryLookup.Values.Where(e => e.SongId == songId))
            {
                if (!entry.IsVideoCached)
                {
                    entry.IsVideoCached = true;
                }

                if (duration.HasValue)
                {
                    entry.VideoLength = FormatVideoDuration(duration.Value);
                    Log.Information("[DJSCREEN CACHE] Applied local duration for SongId={SongId}, QueueId={QueueId}, Length={Length}", songId, entry.QueueId, entry.VideoLength);
                }
            }

            // Only mark verified when we actually captured a duration; otherwise allow re-probe.
            if (duration.HasValue)
            {
                lock (_localCacheLock)
                {
                    _verifiedLocalCacheSongIds.Add(songId);
                }
            }
        }

        private void MarkEntriesAsNotCached(int songId)
        {
            foreach (var entry in _queueEntryLookup.Values.Where(e => e.SongId == songId))
            {
                entry.IsVideoCached = false;
            }

            lock (_localCacheLock)
            {
                _verifiedLocalCacheSongIds.Remove(songId);
            }
        }

        private static string FormatVideoDuration(TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero)
            {
                return string.Empty;
            }

            return duration.TotalHours >= 1
                ? duration.ToString(@"h\:mm\:ss")
                : duration.ToString(@"m\:ss");
        }

        private void UpdateEntryVisibility(QueueEntryViewModel entry)
        {
            if (entry == null)
            {
                return;
            }

            if (!IsVisiblyQueued(entry))
            {
                _hiddenQueueEntryIds.Add(entry.QueueId);
                if (QueueEntriesInternal.Contains(entry))
                {
                    QueueEntriesInternal.Remove(entry);
                }
                return;
            }

            _hiddenQueueEntryIds.Remove(entry.QueueId);
            InsertQueueEntryOrdered(QueueEntriesInternal, entry);
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
            }, DispatcherPriority.Background);
        }

        private void LogQueueSummary(string context)
        {
            var total = QueueEntriesInternal.Count;

            if (string.Equals(context, "Loaded", StringComparison.OrdinalIgnoreCase))
            {
                Log.Information("[DJSCREEN QUEUE] Loaded: {Total} total entries", total);
            }
            else
            {
                Log.Information("[DJSCREEN QUEUE] {Context}: {Total} total entries", context, total);
            }
        }

        private void LogHydrationState(bool fallbackApplied, bool snapshotArrived, int merged, int deduped)
        {
            Log.Information(
                "[DJSCREEN HYDRATE] FallbackApplied={FallbackApplied} SnapshotArrived={SnapshotArrived} Merged={Merged} Deduped={Deduped}",
                fallbackApplied,
                snapshotArrived,
                merged,
                deduped);
        }

        private bool IsSnapshotMergeWindowOpen()
        {
            return _restFallbackApplied
                && _restFallbackAppliedUtc.HasValue
                && DateTime.UtcNow - _restFallbackAppliedUtc.Value <= _snapshotMergeWindow;
        }

        private void ResetSnapshotWindow()
        {
            _restFallbackApplied = false;
            _restFallbackAppliedUtc = null;
        }

        public void UpdateQueueColorsAndRules()
        {
            ApplyQueueRules();

            foreach (var entry in QueueEntriesInternal)
            {
                entry.UpdateStatusBrush();
            }

            if (_overlayBindingsActive)
            {
                AttachQueueEntries(QueueEntriesInternal);
            }

            Log.Information("[DJSCREEN QUEUE] Rules applied: {Count} items", QueueEntriesInternal.Count);
        }

        public void OnInitialQueue(IEnumerable<QueueEntryViewModel> initialQueue)
        {
            _hasReceivedInitialQueue = true;

            foreach (var tracked in _queueEntryLookup.Values.ToList())
            {
                tracked.PropertyChanged -= QueueEntryTracking_PropertyChanged;
            }

            var previousCount = _queueEntryLookup.Count;
            Log.Information(
                "[DJSCREEN QUEUE] OnInitialQueue clearing tracked entries before reload. EventId={EventId}, previousTracked={PreviousTracked}",
                _currentEventId ?? "<none>",
                previousCount);

            _queueEntryLookup.Clear();
            _hiddenQueueEntryIds.Clear();

            var entries = new List<QueueEntryViewModel>();

            if (initialQueue != null)
            {
                foreach (var entry in initialQueue.Where(e => e != null).OrderBy(e => e.Position))
                {
                    var viewModel = entry as QueueEntryViewModel ?? new QueueEntryViewModel(entry);
                    RegisterQueueEntry(viewModel);
                    entries.Add(viewModel);
                }
            }

            DispatcherHelper.RunOnUIThread(() =>
            {
                QueueEntriesInternal.Clear();
                foreach (var entry in entries.Where(IsVisiblyQueued))
                {
                    QueueEntriesInternal.Add(entry);
                }
                UpdateQueueColorsAndRules();
                Log.Information("[DJSCREEN QUEUE] SYNC LOAD: {Count} items", QueueEntriesInternal.Count);
            });
            TryCompleteHydration("SignalR InitialQueue");
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

                if (QueueEntriesInternal.Count == 0)
                {
                    SetWarningMessage("The queue is empty. Nothing to reorder.");
                    return;
                }

                var snapshot = QueueEntriesInternal
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
                    .Select(w => w.FindName("QueueItemsListView") as ListView)
                    .FirstOrDefault(lv => lv != null);

                if (listView == null)
                {
                    Log.Error("[DJSCREEN] Drag failed: QueueItemsListView not found");
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
                int sourceIndex = QueueEntriesInternal.IndexOf(draggedItem);
                int targetIndex = QueueEntriesInternal.IndexOf(targetItem);

                if (sourceIndex < 0 || targetIndex < 0)
                {
                    Log.Warning("[DJSCREEN] Drop failed: Invalid source or target index, SourceIndex={SourceIndex}, TargetIndex={TargetIndex}", sourceIndex, targetIndex);
                    SetWarningMessage("Drop failed: Invalid queue indices.");
                    return;
                }

                Log.Information("[DJSCREEN] Reordering queue locally");
                Application.Current.Dispatcher.Invoke(() =>
                {
                    QueueEntriesInternal.Move(sourceIndex, targetIndex);
                    for (int i = 0; i < QueueEntriesInternal.Count; i++)
                    {
                        QueueEntriesInternal[i].Position = i + 1;
                    }
                });

                var newOrder = QueueEntriesInternal
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
            AutoPlayButtonText = IsAutoPlayEnabled ? "AI-DJ: ON" : "AI-DJ: OFF";
            Log.Information("[DJSCREEN] AutoPlay set to: {State}", IsAutoPlayEnabled);
            UpdateQueueColorsAndRules();
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
                    var entry = QueueEntriesInternal.FirstOrDefault(q => q.QueueId == PlayingQueueEntry.QueueId);
                    if (entry != null)
                    {
                        int sourceIndex = QueueEntriesInternal.IndexOf(entry);
                        if (sourceIndex >= 0)
                        {
                            QueueEntriesInternal.Move(sourceIndex, QueueEntriesInternal.Count - 1);
                            for (int i = 0; i < QueueEntriesInternal.Count; i++)
                            {
                                QueueEntriesInternal[i].Position = i + 1;
                            }
                            var newOrder = QueueEntriesInternal
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
                    OnPropertyChanged(nameof(TotalSongsPlayed));
                    Log.Information("[DJSCREEN] Incremented TotalSongsPlayed: {Count}", TotalSongsPlayed);
                    UnregisterQueueEntry(PlayingQueueEntry);
                    PlayingQueueEntry = null;
                    OnPropertyChanged(nameof(PlayingQueueEntry));
                }
                await LoadQueueData();
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
                for (int i = 0; i < QueueEntriesInternal.Count; i++)
                {
                    QueueEntriesInternal[i].Position = i + 1;
                }

                var newOrder = QueueEntriesInternal
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
            if (string.IsNullOrEmpty(_currentEventId))
            {
                return;
            }

            try
            {
                var shouldMarkInitialQueue = _isHydratingFromSignalR && !_hasReceivedInitialQueue;
                Log.Information("[DJSCREEN] Loading queue data for event: {EventId}", _currentEventId);
                var queueItems = await _apiService.GetQueueAsync(_currentEventId);
                Log.Information("[DJSCREEN] API returned {Count} queue entries for event {EventId}, QueueIds={QueueIds}",
                    queueItems.Count, _currentEventId, string.Join(",", queueItems.Select(q => q.QueueId)));

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var entries = new List<QueueEntryViewModel>();
                    foreach (var dto in queueItems.OrderBy(q => q.Position))
                    {
                        var entry = new QueueEntryViewModel();
                        ApplyDtoToQueueEntry(entry, dto, "Initial");
                        entries.Add(entry);
                    }

                    OnInitialQueue(entries);

                    if (SelectedQueueEntry == null || !QueueEntriesInternal.Contains(SelectedQueueEntry))
                    {
                        SelectedQueueEntry = QueueEntriesInternal.FirstOrDefault();
                        if (SelectedQueueEntry != null)
                        {
                            OnPropertyChanged(nameof(SelectedQueueEntry));
                        }
                    }

                    LogQueueSummary("Loaded");
                    Log.Information("[DJSCREEN] Loaded {Count} queue entries for event {EventId}", QueueEntriesInternal.Count, _currentEventId);
                    SyncQueueSingerStatuses();
                });

                if (shouldMarkInitialQueue)
                {
                    _hasReceivedInitialQueue = true;
                    TryCompleteHydration("REST LoadQueue");
                    _restFallbackApplied = true;
                    _restFallbackAppliedUtc = DateTime.UtcNow;
                    LogHydrationState(fallbackApplied: true, snapshotArrived: false, merged: queueItems.Count, deduped: 0);
                }

                _initialQueueTcs?.TrySetResult(true);
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to load queue for EventId={EventId}: {Message}", _currentEventId, ex.Message);
                SetWarningMessage($"Failed to load queue: {ex.Message}");
            }
        }

        private void HandleInitialQueue(List<DJQueueItemDto> queue)
        {
            var fallbackApplied = _restFallbackApplied;
            var shouldMerge = IsSnapshotMergeWindowOpen();

            DispatcherHelper.RunOnUIThread(() =>
            {
                if (shouldMerge)
                {
                    var (mergedCount, dedupedCount) = MergeSnapshotIntoExisting(queue);
                    Log.Information("[HYDRATION MERGE] reconciled={Reconciled} deduped={Deduped}", mergedCount, dedupedCount);
                    RefreshQueueOrdering();
                    LogQueueSummary("Snapshot Merge");
                    SyncQueueSingerStatuses();
                    LogHydrationState(fallbackApplied, snapshotArrived: true, mergedCount, dedupedCount);
                    TryCompleteHydration("SignalR InitialQueue (Merged)");
                }
                else
                {
                    var entries = new List<QueueEntryViewModel>();
                    var orderedDtos = (queue ?? new List<DJQueueItemDto>()).OrderBy(q => q.Position);
                    foreach (var dto in orderedDtos)
                    {
                        var entry = new QueueEntryViewModel();
                        ApplyDtoToQueueEntry(entry, dto, "Initial");
                        entries.Add(entry);
                    }

                    OnInitialQueue(entries);

                    LogQueueSummary("Loaded");
                    SyncQueueSingerStatuses();
                    LogHydrationState(fallbackApplied, snapshotArrived: true, entries.Count, 0);
                }

                _initialQueueTcs?.TrySetResult(true);
            });

            if (shouldMerge || fallbackApplied)
            {
                ResetSnapshotWindow();
            }
        }

        private (int mergedCount, int dedupedCount) MergeSnapshotIntoExisting(List<DJQueueItemDto> queue)
        {
            var mergedCount = 0;
            var dedupedCount = 0;
            var orderedDtos = (queue ?? new List<DJQueueItemDto>())
                .Where(dto => dto != null)
                .OrderBy(dto => dto.Position)
                .ToList();
            var seenQueueIds = new HashSet<int>();
            var uniqueDtos = new List<DJQueueItemDto>();

            foreach (var dto in orderedDtos)
            {
                if (dto.QueueId <= 0)
                {
                    continue;
                }

                if (!seenQueueIds.Add(dto.QueueId))
                {
                    dedupedCount++;
                    continue;
                }

                uniqueDtos.Add(dto);
            }

            foreach (var dto in uniqueDtos)
            {
                var entry = GetTrackedQueueEntry(dto.QueueId);
                if (entry == null)
                {
                    entry = new QueueEntryViewModel();
                    ApplyDtoToQueueEntry(entry, dto, "SnapshotMerge");
                    RegisterQueueEntry(entry);
                }
                else
                {
                    ApplyDtoToQueueEntry(entry, dto, "SnapshotMerge");
                    dedupedCount++;
                }

                UpdateEntryVisibility(entry);
                mergedCount++;
            }

            var snapshotIds = new HashSet<int>(uniqueDtos.Select(dto => dto.QueueId));
            foreach (var trackedId in _queueEntryLookup.Keys.ToList())
            {
                if (!snapshotIds.Contains(trackedId) && _queueEntryLookup.TryGetValue(trackedId, out var staleEntry))
                {
                    UnregisterQueueEntry(staleEntry);
                }
            }

            return (mergedCount, dedupedCount);
        }

        private void HandleQueueItemAdded(DJQueueItemDto item)
        {
            HandleQueueItemUpsert(item, isAdd: true);
        }

        private void HandleQueueItemUpdated(DJQueueItemDto item)
        {
            HandleQueueItemUpsert(item, isAdd: false);
        }

        private void HandleQueueItemUpsert(DJQueueItemDto item, bool isAdd)
        {
            if (item == null)
            {
                return;
            }

            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var sourceLabel = isAdd ? "Added" : "Updated";
                    var existing = GetTrackedQueueEntry(item.QueueId);
                    if (existing == null)
                    {
                        var entry = new QueueEntryViewModel();
                        ApplyDtoToQueueEntry(entry, item, sourceLabel);
                        RegisterQueueEntry(entry);
                        UpdateEntryVisibility(entry);
                        Log.Information("[DJSCREEN SIGNALR] Added queue entry {QueueId} via SignalR", entry.QueueId);
                    }
                    else
                    {
                        ApplyDtoToQueueEntry(existing, item, sourceLabel);
                        UpdateEntryVisibility(existing);
                        Log.Information("[DJSCREEN SIGNALR] Updated queue entry {QueueId} via SignalR", existing.QueueId);
                    }

                    RefreshQueueOrdering();
                    LogQueueSummary("Updated");
                    SyncQueueSingerStatuses();
                }
                catch (Exception ex)
                {
                    var actionLabel = isAdd ? "Added" : "Updated";
                    Log.Error(ex, "[DJSCREEN SIGNALR] Failed to handle QueueItem{Action}V2 for QueueId={QueueId}", actionLabel, item.QueueId);
                    SetWarningMessage($"Failed to update queue: {ex.Message}");
                }
            });
        }

        private void HandleQueueItemRemoved(int queueId)
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var existing = GetTrackedQueueEntry(queueId);
                    if (existing != null)
                    {
                        UnregisterQueueEntry(existing);
                        RefreshQueueOrdering();
                        LogQueueSummary("Updated");
                        SyncQueueSingerStatuses();
                        Log.Information("[DJSCREEN SIGNALR] Removed queue entry {QueueId} via SignalR", queueId);
                    }
                    else
                    {
                        Log.Warning("[DJSCREEN SIGNALR] Received removal for unknown QueueId={QueueId}", queueId);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[DJSCREEN SIGNALR] Failed to handle QueueItemRemovedV2 for QueueId={QueueId}", queueId);
                    SetWarningMessage($"Failed to update queue: {ex.Message}");
                }
            });
        }

    }
}
