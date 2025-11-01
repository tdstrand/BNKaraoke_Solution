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
        private bool _overlayBindingsActive;

        partial void OnPlayingQueueEntryChanged(QueueEntry? value)
        {
            if (!_overlayBindingsActive)
            {
                return;
            }

            UpdateOverlayState();
        }

        partial void OnCurrentEventChanged(EventDto? value)
        {
            if (!_overlayBindingsActive)
            {
                return;
            }

            UpdateOverlayState();
        }

        partial void OnQueueEntriesChanged(ObservableCollection<QueueEntry> value)
        {
            if (!_overlayBindingsActive)
            {
                return;
            }

            AttachQueueEntries(value);
            UpdateOverlayState();
        }

        private void EnsureOverlayBindingsActive()
        {
            if (_overlayBindingsActive)
            {
                return;
            }

            _overlayBindingsActive = true;
            AttachQueueEntries(QueueEntries);
            UpdateOverlayState();
        }

        private void DeactivateOverlayBindings()
        {
            if (!_overlayBindingsActive && _trackedQueueEntries == null)
            {
                return;
            }

            _overlayBindingsActive = false;
            AttachQueueEntries(null);
            _entriesWithHandlers.Clear();

            var overlay = OverlayViewModel.Instance;
            overlay.IsBlueState = true;
            overlay.UpdatePlaybackState(new List<QueueEntry>(), null, null, GetCurrentMatureMode());
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
            if (!_overlayBindingsActive)
            {
                return;
            }

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
            if (!_overlayBindingsActive)
            {
                return;
            }

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
            if (!_overlayBindingsActive)
            {
                return;
            }

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
