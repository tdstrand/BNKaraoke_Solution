using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BNKaraoke.DJ.Models
{
    public record ReorderQueuePreviewItem
    {
        [JsonConstructor]
        public ReorderQueuePreviewItem(
            int queueId,
            int originalIndex,
            int displayIndex,
            string songTitle,
            string songArtist,
            string requestor,
            bool isMature,
            bool isLocked,
            bool isDeferred,
            int movement,
            IReadOnlyList<string> reasons)
        {
            QueueId = queueId;
            OriginalIndex = originalIndex;
            DisplayIndex = displayIndex;
            SongTitle = songTitle;
            SongArtist = songArtist;
            Requestor = requestor;
            IsMature = isMature;
            IsLocked = isLocked;
            IsDeferred = isDeferred;
            Movement = movement;
            Reasons = reasons ?? Array.Empty<string>();
        }

        public int QueueId { get; init; }

        public int OriginalIndex { get; init; }

        public int DisplayIndex { get; init; }

        public string SongTitle { get; init; }

        public string SongArtist { get; init; }

        public string Requestor { get; init; }

        public bool IsMature { get; init; }

        public bool IsLocked { get; init; }

        public bool IsDeferred { get; init; }

        public int Movement { get; init; }

        public IReadOnlyList<string> Reasons { get; init; }

        public string DisplayLabel => $"{DisplayIndex + 1}. {SongTitle}";

        public string SecondaryLine
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(SongArtist) && !string.IsNullOrWhiteSpace(Requestor))
                {
                    return $"{SongArtist} — {Requestor}";
                }

                if (!string.IsNullOrWhiteSpace(SongArtist))
                {
                    return SongArtist;
                }

                return Requestor;
            }
        }

        public bool ShowMovement => Movement != 0;

        public string MovementDescription => Movement switch
        {
            > 0 => $"↓ moved {Movement}",
            < 0 => $"↑ moved {Math.Abs(Movement)}",
            _ => ""
        };

        public bool ShowMaturePill => IsMature;

        public string MaturePillText => IsDeferred ? "Deferred (Mature)" : "Mature";

        public string Tooltip => Reasons.Count == 0
            ? "No optimization reasoning available."
            : string.Join(Environment.NewLine, Reasons);

        public static ReorderQueuePreviewItem FromQueueEntry(int index, QueueEntry entry, bool isLocked)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            var title = string.IsNullOrWhiteSpace(entry.SongTitle) ? "Unknown Song" : entry.SongTitle!;
            var artist = string.IsNullOrWhiteSpace(entry.SongArtist) ? string.Empty : entry.SongArtist!;
            var requestor = !string.IsNullOrWhiteSpace(entry.RequestorDisplayName)
                ? entry.RequestorDisplayName!
                : !string.IsNullOrWhiteSpace(entry.RequestorUserName)
                    ? entry.RequestorUserName!
                    : "Unknown Singer";

            return new ReorderQueuePreviewItem(
                entry.QueueId,
                index,
                index,
                title,
                artist,
                requestor,
                entry.IsMature,
                isLocked,
                false,
                0,
                Array.Empty<string>());
        }
    }
}
