using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using BNKaraoke.DJ.Models;
using BNKaraoke.DJ.ViewModels.Overlays;

namespace BNKaraoke.DJ.ViewModels
{
    public partial class DJScreenViewModel
    {
        private readonly HashSet<QueueEntry> _entriesWithHandlers = new();
        private ObservableCollection<QueueEntry>? _trackedQueueEntries;

        partial void OnPlayingQueueEntryChanged(QueueEntry? value)
        {
            UpdateOverlayState();
        }

        partial void OnCurrentEventChanged(EventDto? value)
        {
            UpdateOverlayState();
        }

        partial void OnQueueEntriesChanged(ObservableCollection<QueueEntry> value)
        {
            AttachQueueEntries(value);
            UpdateOverlayState();
        }

        private void InitializeOverlayBindings()
        {
            AttachQueueEntries(QueueEntries);
            UpdateOverlayState();
        }

        private void AttachQueueEntries(ObservableCollection<QueueEntry>? entries)
        {
            if (_trackedQueueEntries != null)
            {
                _trackedQueueEntries.CollectionChanged -= QueueEntries_CollectionChanged;
                foreach (var entry in _trackedQueueEntries)
                {
                    if (_entriesWithHandlers.Remove(entry))
                    {
                        entry.PropertyChanged -= QueueEntry_PropertyChanged;
                    }
                }
            }

            _trackedQueueEntries = entries;

            if (_trackedQueueEntries != null)
            {
                _trackedQueueEntries.CollectionChanged += QueueEntries_CollectionChanged;
                foreach (var entry in _trackedQueueEntries)
                {
                    if (_entriesWithHandlers.Add(entry))
                    {
                        entry.PropertyChanged += QueueEntry_PropertyChanged;
                    }
                }
            }
        }

        private void QueueEntries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var entry in _entriesWithHandlers.ToList())
                {
                    entry.PropertyChanged -= QueueEntry_PropertyChanged;
                }

                _entriesWithHandlers.Clear();
                if (_trackedQueueEntries != null)
                {
                    foreach (var entry in _trackedQueueEntries)
                    {
                        if (_entriesWithHandlers.Add(entry))
                        {
                            entry.PropertyChanged += QueueEntry_PropertyChanged;
                        }
                    }
                }
                UpdateOverlayState();
                return;
            }

            if (e.OldItems != null)
            {
                foreach (QueueEntry entry in e.OldItems)
                {
                    if (_entriesWithHandlers.Remove(entry))
                    {
                        entry.PropertyChanged -= QueueEntry_PropertyChanged;
                    }
                }
            }

            if (e.NewItems != null)
            {
                foreach (QueueEntry entry in e.NewItems)
                {
                    if (_entriesWithHandlers.Add(entry))
                    {
                        entry.PropertyChanged += QueueEntry_PropertyChanged;
                    }
                }
            }

            UpdateOverlayState();
        }

        private void QueueEntry_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(QueueEntry.IsUpNext)
                or nameof(QueueEntry.IsCurrentlyPlaying)
                or nameof(QueueEntry.RequestorDisplayName)
                or nameof(QueueEntry.RequestorUserName)
                or nameof(QueueEntry.SongTitle)
                or nameof(QueueEntry.SongArtist)
                or nameof(QueueEntry.IsActive)
                or nameof(QueueEntry.IsOnHold)
                or nameof(QueueEntry.Position))
            {
                UpdateOverlayState();
            }
        }

        private void UpdateOverlayState()
        {
            var overlay = OverlayViewModel.Instance;
            var playing = PlayingQueueEntry;
            var currentEvent = CurrentEvent;
            overlay.IsBlueState = playing == null;

            var queueSnapshot = QueueEntries?.ToList() ?? new List<QueueEntry>();
            overlay.UpdatePlaybackState(queueSnapshot, playing, currentEvent, GetCurrentMatureMode());
        }

        private ReorderMode GetCurrentMatureMode()
        {
            if (_userSessionService.PreferredReorderMode.HasValue)
            {
                return _userSessionService.PreferredReorderMode.Value;
            }

            var defaultPolicy = _settingsService.Settings.DefaultReorderMaturePolicy;
            return string.Equals(defaultPolicy, "Allow", StringComparison.OrdinalIgnoreCase)
                ? ReorderMode.AllowMature
                : ReorderMode.DeferMature;
        }
    }
}
