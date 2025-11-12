using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Media;
using BNKaraoke.DJ.Models;
using BNKaraoke.DJ.Services.Presentation;

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
        private readonly List<Singer> _linkedSingers = new();
        private SingerStatusFlags _singerStatus = SingerStatusFlags.None;
        private SolidColorBrush _singerStatusBrush = SingerStyleMapper.DefaultForeground();

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

        public bool IsReady => IsActive && IsSingerLoggedIn && IsSingerJoined && !IsSingerOnBreak && !ShowAsOnHold && !IsPlayed && !IsCurrentlyPlaying;

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

        public IReadOnlyList<Singer> LinkedSingers => _linkedSingers;

        public SingerStatusFlags SingerStatus
        {
            get => _singerStatus;
            private set
            {
                if (_singerStatus != value)
                {
                    _singerStatus = value;
                    base.OnPropertyChanged(nameof(SingerStatus));
                    SingerStatusBrush = SingerStyleMapper.MapForeground(_singerStatus);
                }
            }
        }

        public SolidColorBrush SingerStatusBrush
        {
            get => _singerStatusBrush;
            private set
            {
                if (!ReferenceEquals(_singerStatusBrush, value))
                {
                    _singerStatusBrush = value;
                    base.OnPropertyChanged(nameof(SingerStatusBrush));
                }
            }
        }

        public void ApplySingerStatus(SingerStatusDto? singerDto)
        {
            if (singerDto == null)
            {
                SingerStatus = SingerStatusFlags.None;
                SingerStatusBrush = SingerStyleMapper.DefaultForeground();
                return;
            }

            var flags = singerDto.Flags;
            if (flags == SingerStatusFlags.None)
            {
                if (singerDto.IsLoggedIn)
                {
                    flags |= SingerStatusFlags.LoggedIn;
                }
                if (singerDto.IsJoined)
                {
                    flags |= SingerStatusFlags.Joined;
                }
                if (singerDto.IsOnBreak)
                {
                    flags |= SingerStatusFlags.OnBreak;
                }
            }

            SingerStatus = flags;
            IsSingerLoggedIn = singerDto.IsLoggedIn;
            IsSingerJoined = singerDto.IsJoined;
            IsSingerOnBreak = singerDto.IsOnBreak;

            if (!string.IsNullOrWhiteSpace(singerDto.DisplayName))
            {
                RequestorDisplayName = singerDto.DisplayName;
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

        public void UpdateLinkedSingers(IEnumerable<Singer?>? singers)
        {
            DetachLinkedSingers();

            if (singers != null)
            {
                foreach (var singer in singers)
                {
                    if (singer == null)
                    {
                        continue;
                    }

                    _linkedSingers.Add(singer);
                    singer.PropertyChanged -= LinkedSingerOnPropertyChanged;
                    singer.PropertyChanged += LinkedSingerOnPropertyChanged;
                }
            }

            RecalculateSingerStatus();
            base.OnPropertyChanged(nameof(LinkedSingers));
        }

        private void DetachLinkedSingers()
        {
            if (_linkedSingers.Count == 0)
            {
                return;
            }

            foreach (var singer in _linkedSingers)
            {
                singer.PropertyChanged -= LinkedSingerOnPropertyChanged;
            }

            _linkedSingers.Clear();
        }

        private void LinkedSingerOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Singer.Status)
                || e.PropertyName == nameof(Singer.StatusBrush)
                || e.PropertyName == nameof(Singer.IsLoggedIn)
                || e.PropertyName == nameof(Singer.IsJoined)
                || e.PropertyName == nameof(Singer.IsOnBreak))
            {
                RecalculateSingerStatus();
            }
        }

        private void RecalculateSingerStatus()
        {
            if (_linkedSingers.Count == 0)
            {
                SingerStatus = SingerStatusFlags.None;
                SingerStatusBrush = SingerStyleMapper.DefaultForeground();
                return;
            }

            var combined = SingerStatusFlags.None;
            foreach (var singer in _linkedSingers)
            {
                combined |= singer.Status;
            }

            SingerStatus = combined;
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
