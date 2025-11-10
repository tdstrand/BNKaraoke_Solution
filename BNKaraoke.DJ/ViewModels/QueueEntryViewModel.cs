using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using BNKaraoke.DJ.Models;

namespace BNKaraoke.DJ.ViewModels
{
    public class QueueEntryViewModel : QueueEntry
    {
        private static readonly Brush DefaultStatusBrush = Brushes.Transparent;
        private static readonly HashSet<string> PlayedStatuses = new(StringComparer.OrdinalIgnoreCase)
        {
            "Sung",
            "Skipped",
            "Archived",
            "Completed",
            "Done"
        };

        private Brush _statusBrush = DefaultStatusBrush;

        public QueueEntryViewModel()
        {
        }

        public QueueEntryViewModel(QueueEntry entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            QueueId = entry.QueueId;
            EventId = entry.EventId;
            SongId = entry.SongId;
            SongTitle = entry.SongTitle;
            SongArtist = entry.SongArtist;
            RequestorDisplayName = entry.RequestorDisplayName;
            RequestorUserName = entry.RequestorUserName;
            Position = entry.Position;
            Status = entry.Status;
            IsActive = entry.IsActive;
            WasSkipped = entry.WasSkipped;
            IsCurrentlyPlaying = entry.IsCurrentlyPlaying;
            SungAt = entry.SungAt;
            Genre = entry.Genre;
            Decade = entry.Decade;
            YouTubeUrl = entry.YouTubeUrl;
            IsVideoCached = entry.IsVideoCached;
            IsServerCached = entry.IsServerCached;
            IsMature = entry.IsMature;
            IsOnBreak = entry.IsOnBreak;
            IsOnHold = entry.IsOnHold;
            IsUpNext = entry.IsUpNext;
            HoldReason = entry.HoldReason;
            IsSingerLoggedIn = entry.IsSingerLoggedIn;
            IsSingerJoined = entry.IsSingerJoined;
            IsSingerOnBreak = entry.IsSingerOnBreak;
            NormalizationGain = entry.NormalizationGain;
            FadeStartTime = entry.FadeStartTime;
            IntroMuteDuration = entry.IntroMuteDuration;
            VideoLength = entry.VideoLength;

            if (entry.Singers != null)
            {
                Singers = entry.Singers.ToList();
            }

            UpdateStatusBrush();
        }

        public bool IsPlayed => SungAt != null || WasSkipped || (Status != null && PlayedStatuses.Contains(Status));

        public bool IsReady => IsActive && IsSingerLoggedIn && IsSingerJoined && !IsSingerOnBreak && !IsOnHold && !IsPlayed && !IsCurrentlyPlaying;

        public bool ShowAsOnHold => IsOnBreak || IsSingerOnBreak || !IsSingerLoggedIn || !IsSingerJoined;

        public Brush HoldIndicatorBrush => ShowAsOnHold ? Brushes.Red : Brushes.Transparent;

        public Brush StatusBrush
        {
            get => _statusBrush;
            private set
            {
                if (!ReferenceEquals(_statusBrush, value))
                {
                    _statusBrush = value;
                    base.OnPropertyChanged(nameof(StatusBrush));
                }
            }
        }

        protected override void OnPropertyChanged(string? propertyName = null)
        {
            base.OnPropertyChanged(propertyName);

            if (propertyName is nameof(SungAt) or nameof(WasSkipped) or nameof(Status))
            {
                base.OnPropertyChanged(nameof(IsPlayed));
                UpdateStatusBrush();
            }

            if (propertyName is nameof(IsSingerLoggedIn)
                or nameof(IsSingerJoined)
                or nameof(IsSingerOnBreak)
                or nameof(IsOnBreak)
                or nameof(IsActive)
                or nameof(IsOnHold)
                or nameof(IsCurrentlyPlaying)
                or nameof(SungAt)
                or nameof(WasSkipped)
                or nameof(Status))
            {
                base.OnPropertyChanged(nameof(IsReady));
                base.OnPropertyChanged(nameof(ShowAsOnHold));
                base.OnPropertyChanged(nameof(HoldIndicatorBrush));
                UpdateStatusBrush();
            }
        }

        public void UpdateStatusBrush()
        {
            if (IsCurrentlyPlaying)
            {
                StatusBrush = Brushes.LightGreen;
                return;
            }

            if (IsUpNext)
            {
                StatusBrush = Brushes.LightGoldenrodYellow;
                return;
            }

            if (IsOnHold)
            {
                StatusBrush = Brushes.LightGray;
                return;
            }

            if (IsPlayed)
            {
                StatusBrush = Brushes.LightSlateGray;
                return;
            }

            StatusBrush = DefaultStatusBrush;
        }
    }
}
