using System;
using System.Collections.Generic;

namespace BNKaraoke.DJ.Models
{
    public record ReorderQueuePreviewItem(
        int QueueId,
        int OriginalIndex,
        int DisplayIndex,
        string SongTitle,
        string SongArtist,
        string Requestor,
        bool IsMature,
        bool IsLocked,
        bool IsDeferred,
        int Movement,
        IReadOnlyList<string> Reasons)
    {
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
