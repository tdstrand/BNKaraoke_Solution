using System.Collections.Generic;

namespace BNKaraoke.Api.Controllers
{
    internal sealed class PreviewQueueState
    {
        public int QueueId { get; init; }
        public int OriginalIndex { get; init; }
        public int Position { get; init; }
        public string RequestorUserName { get; init; } = string.Empty;
        public string RequestorDisplayName { get; init; } = string.Empty;
        public string SongTitle { get; init; } = string.Empty;
        public string SongArtist { get; init; } = string.Empty;
        public bool IsMature { get; init; }
        public bool IsLocked { get; init; }
        public int Movement { get; set; }
        public bool IsDeferred { get; set; }
        public int DisplayIndex { get; set; }
        public List<string> Reasons { get; } = new();
    }

    internal sealed record PlanAssignmentDto(int QueueId, int Position);
}
