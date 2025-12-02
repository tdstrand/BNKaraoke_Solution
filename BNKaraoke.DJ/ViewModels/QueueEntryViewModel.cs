using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Media;
using BNKaraoke.DJ.Models;
using BNKaraoke.DJ.Services.Presentation;
using Serilog;

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
        private bool _isReady;

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

            RecomputeIsReady();
        }

        public void ApplyV2QueueItem(DJQueueItemDto dto)
        {
            if (dto == null)
            {
                throw new ArgumentNullException(nameof(dto));
            }

            var singerWasNull = dto.Singer == null;
            var effectiveSinger = dto.Singer ?? BuildFallbackSinger(dto);

            Log.Information(
                "[QUEUEENTRY APPLY] ApplyV2QueueItem start QueueId={QueueId} SingerNull={SingerNull} EffectiveFlags={Flags}",
                dto.QueueId,
                singerWasNull,
                effectiveSinger.Flags);

            QueueId = dto.QueueId;
            EventId = dto.EventId;
            SongId = dto.SongId ?? SongId;
            SongTitle = dto.SongTitle;
            SongArtist = dto.SongArtist;
            YouTubeUrl = dto.YouTubeUrl;
            RequestorUserName = dto.RequestorUserName;
            RequestorDisplayName = !string.IsNullOrWhiteSpace(dto.RequestorDisplayName)
                ? dto.RequestorDisplayName
                : !string.IsNullOrWhiteSpace(effectiveSinger.DisplayName)
                    ? effectiveSinger.DisplayName
                    : dto.RequestorUserName;
            Singers = dto.Singers != null ? new List<string>(dto.Singers) : new List<string>();
            Position = dto.Position;
            Status = dto.Status;
            IsActive = dto.IsActive;
            WasSkipped = dto.WasSkipped;
            IsCurrentlyPlaying = dto.IsCurrentlyPlaying;
            SungAt = dto.SungAt?.UtcDateTime;
            IsUpNext = dto.IsUpNext;
            IsServerCached = dto.IsServerCached;
            IsMature = dto.IsMature;
            NormalizationGain = dto.NormalizationGain.HasValue ? (float?)dto.NormalizationGain.Value : null;
            if (dto.FadeStartTime.HasValue)
            {
                FadeStartTime = ToSeconds(dto.FadeStartTime);
            }

            if (dto.IntroMuteDuration.HasValue)
            {
                IntroMuteDuration = ToSeconds(dto.IntroMuteDuration);
            }

            var holdReason = NormalizeHoldReason(dto.HoldReason);
            IsOnHold = holdReason != null;
            HoldReason = holdReason;

            IsOnBreak = effectiveSinger.IsOnBreak;

            IsSingerLoggedIn = dto.IsSingerLoggedIn ?? IsSingerLoggedIn;
            IsSingerJoined = dto.IsSingerJoined ?? IsSingerJoined;
            IsSingerOnBreak = dto.IsSingerOnBreak ?? IsSingerOnBreak;

            ApplySingerStatus(effectiveSinger);
            Log.Information(
                "[QUEUEENTRY APPLY] QueueId={QueueId} ApplySingerStatus completed prior to readiness refresh (Flags={Flags})",
                QueueId,
                SingerStatus);
            RecomputeIsReady();

            Log.Information(
                "[QUEUEENTRY APPLY] QueueId={QueueId} Singer:LoggedIn={LoggedIn}, Joined={Joined}, OnBreak={OnBreak} -> Brush={Brush}",
                QueueId,
                IsSingerLoggedIn,
                IsSingerJoined,
                IsSingerOnBreak || IsOnBreak,
                DescribeBrush(StatusBrush));
        }

        public bool IsPlayed => SungAt != null || WasSkipped || (Status != null && PlayedStatuses.Contains(Status));

        public bool IsReady
        {
            get => _isReady;
            private set
            {
                if (_isReady == value)
                {
                    return;
                }

                _isReady = value;
                base.OnPropertyChanged(nameof(IsReady));
                base.OnPropertyChanged(nameof(ShowAsOnHold));
                base.OnPropertyChanged(nameof(HoldIndicatorBrush));
            }
        }

        public bool ShowAsOnHold => !IsReady;

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
                }

                SingerStatusBrush = SingerStyleMapper.MapForeground(value);
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
                Log.Debug("[QUEUEENTRY APPLY] ApplySingerStatus fallback to logged-out defaults for QueueId={QueueId}", QueueId);
                SingerStatus = SingerStatusFlags.None;
                IsSingerLoggedIn = false;
                IsSingerJoined = false;
                IsSingerOnBreak = false;
                RecomputeIsReady();
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

            RecomputeIsReady();
        }

        protected override void OnPropertyChanged(string? propertyName = null)
        {
            base.OnPropertyChanged(propertyName);

            if (propertyName is nameof(SungAt) or nameof(WasSkipped) or nameof(Status))
            {
                base.OnPropertyChanged(nameof(IsPlayed));
                UpdateStatusBrush();
            }
            else if (propertyName is nameof(IsCurrentlyPlaying) or nameof(IsUpNext))
            {
                UpdateStatusBrush();
            }

            if (propertyName is nameof(IsSingerLoggedIn)
                or nameof(IsSingerJoined)
                or nameof(IsSingerOnBreak)
                or nameof(SingerStatus)
                or nameof(IsOnBreak)
                or nameof(IsOnHold))
            {
                RecomputeIsReady();
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
                Log.Debug("[QUEUEENTRY APPLY] No roster singers linked for QueueId={QueueId}; preserving DTO singer status", QueueId);
                return;
            }

            var combined = SingerStatusFlags.None;
            foreach (var singer in _linkedSingers)
            {
                combined |= singer.Status;
            }

            SingerStatus = combined;
            Log.Debug("[QUEUEENTRY APPLY] Roster merge updated singer flags for QueueId={QueueId}: Flags={Flags}", QueueId, SingerStatus);
        }

        private void RecomputeIsReady()
        {
            var wasReady = IsReady;
            var loggedIn = SingerStatus.HasFlag(SingerStatusFlags.LoggedIn) || IsSingerLoggedIn;
            var joined = SingerStatus.HasFlag(SingerStatusFlags.Joined) || IsSingerJoined;
            var onBreak = IsOnBreak || IsSingerOnBreak || SingerStatus.HasFlag(SingerStatusFlags.OnBreak);
            var holdActive = IsOnHold;

            IsReady = loggedIn && joined && !onBreak && !holdActive;

            if (IsReady != wasReady)
            {
                Log.Information(
                    "[QUEUEENTRY READY] QueueId={QueueId} Ready={IsReady} (LoggedIn={LoggedIn}, Joined={Joined}, OnBreak={OnBreak}, Hold={Hold})",
                    QueueId,
                    IsReady,
                    loggedIn,
                    joined,
                    onBreak,
                    holdActive);
            }

            UpdateStatusBrush();
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

            if (IsPlayed)
            {
                StatusBrush = Brushes.LightSlateGray;
                return;
            }

            StatusBrush = ResolveAvailabilityBrush();
        }

        private Brush ResolveAvailabilityBrush()
        {
            var onBreak = IsOnBreak || IsSingerOnBreak || SingerStatus.HasFlag(SingerStatusFlags.OnBreak);
            if (onBreak)
            {
                return Brushes.Goldenrod;
            }

            var holdActive = IsOnHold && !IsPlayed;
            if (holdActive)
            {
                return Brushes.Orange;
            }

            return IsReady ? Brushes.Green : Brushes.DarkRed;
        }

        private static float? ToSeconds(TimeSpan? duration)
        {
            return duration.HasValue ? (float)duration.Value.TotalSeconds : null;
        }

        private static string? NormalizeHoldReason(string? holdReason)
        {
            if (string.IsNullOrWhiteSpace(holdReason)
                || string.Equals(holdReason, "None", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return holdReason;
        }

        private static SingerStatusDto BuildFallbackSinger(DJQueueItemDto dto)
        {
            var flags = SingerStatusFlags.None;
            if (dto.IsSingerLoggedIn == true)
            {
                flags |= SingerStatusFlags.LoggedIn;
            }
            if (dto.IsSingerJoined == true)
            {
                flags |= SingerStatusFlags.Joined;
            }
            if (dto.IsSingerOnBreak == true)
            {
                flags |= SingerStatusFlags.OnBreak;
            }

            return new SingerStatusDto
            {
                UserId = dto.RequestorUserName,
                DisplayName = dto.RequestorDisplayName ?? dto.RequestorUserName,
                IsLoggedIn = dto.IsSingerLoggedIn ?? false,
                IsJoined = dto.IsSingerJoined ?? false,
                IsOnBreak = dto.IsSingerOnBreak ?? false,
                Flags = flags
            };
        }

        private static string DescribeBrush(Brush brush)
        {
            if (brush == null)
            {
                return "<null>";
            }

            if (ReferenceEquals(brush, Brushes.LightGreen)) return "LightGreen";
            if (ReferenceEquals(brush, Brushes.LightGoldenrodYellow)) return "LightGoldenrodYellow";
            if (ReferenceEquals(brush, Brushes.LightSlateGray)) return "LightSlateGray";
            if (ReferenceEquals(brush, Brushes.Goldenrod)) return "Goldenrod";
            if (ReferenceEquals(brush, Brushes.Orange)) return "Orange";
            if (ReferenceEquals(brush, Brushes.DarkRed)) return "DarkRed";
            if (ReferenceEquals(brush, Brushes.Green)) return "Green";
            if (ReferenceEquals(brush, Brushes.Gray)) return "Gray";
            if (ReferenceEquals(brush, Brushes.Transparent)) return "Transparent";

            if (brush is SolidColorBrush solid)
            {
                return solid.Color.ToString();
            }

            return brush.ToString();
        }
    }
}
