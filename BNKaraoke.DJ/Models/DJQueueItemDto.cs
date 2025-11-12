using System;
using System.Collections.Generic;

namespace BNKaraoke.DJ.Models
{
    /// <summary>
    /// Unified queue item payload delivered by the V2 SignalR events and REST V2.
    /// </summary>
    public class DJQueueItemDto
    {
        // Identity
        public int QueueId { get; set; }
        public int EventId { get; set; }

        // Song metadata
        public int SongId { get; set; }
        public string SongTitle { get; set; } = string.Empty;
        public string SongArtist { get; set; } = string.Empty;
        public string? YouTubeUrl { get; set; }

        // Requestor + singers
        public string RequestorUserName { get; set; } = string.Empty;
        public string? RequestorDisplayName { get; set; }
        public List<string> Singers { get; set; } = new();

        // Embedded singer status (source of truth for coloring)
        public SingerStatusDto Singer { get; set; } = new();

        // Queue state
        public int Position { get; set; }
        public string Status { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool WasSkipped { get; set; }
        public bool IsCurrentlyPlaying { get; set; }
        public DateTime? SungAt { get; set; }
        public bool IsUpNext { get; set; }
        public string HoldReason { get; set; } = string.Empty;

        // Legacy mirrors (safe during transition)
        public bool IsSingerLoggedIn { get; set; }
        public bool IsSingerJoined { get; set; }
        public bool IsSingerOnBreak { get; set; }

        // Media metadata
        public bool IsServerCached { get; set; }
        public bool IsMature { get; set; }
        public float? NormalizationGain { get; set; }
        public float? FadeStartTime { get; set; }
        public float? IntroMuteDuration { get; set; }
    }
}
