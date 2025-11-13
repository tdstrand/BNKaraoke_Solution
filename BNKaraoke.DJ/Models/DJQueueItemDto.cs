using System;
using System.Collections.Generic;

namespace BNKaraoke.DJ.Models
{
    /// <summary>
    /// Unified queue item payload delivered by the V2 SignalR events and REST V2.
    /// </summary>
    public sealed class DJQueueItemDto
    {
        // Identity
        public int QueueId { get; set; }
        public int EventId { get; set; }

        // Song
        public int? SongId { get; set; }
        public string SongTitle { get; set; } = "";
        public string SongArtist { get; set; } = "";
        public string? YouTubeUrl { get; set; }

        // Requestor / Singers
        public string RequestorUserName { get; set; } = "";
        public string? RequestorDisplayName { get; set; }
        public List<string> Singers { get; set; } = new();

        // Singer snapshot (source of truth for color/status)
        public SingerStatusDto Singer { get; set; } = new();

        // Queue state
        public int Position { get; set; }
        public string Status { get; set; } = "";
        public bool IsActive { get; set; }
        public bool WasSkipped { get; set; }
        public bool IsCurrentlyPlaying { get; set; }
        public DateTimeOffset? SungAt { get; set; }
        public bool IsOnBreak { get; set; }
        public bool IsUpNext { get; set; }
        public string HoldReason { get; set; } = "";

        // Legacy mirrors (retain for existing bindings/logic)
        public bool? IsSingerLoggedIn { get; set; }
        public bool? IsSingerJoined { get; set; }
        public bool? IsSingerOnBreak { get; set; }

        // Media / playback hints
        public bool IsServerCached { get; set; }
        public bool IsMature { get; set; }
        public double? NormalizationGain { get; set; }
        public TimeSpan? FadeStartTime { get; set; }
        public TimeSpan? IntroMuteDuration { get; set; }
    }
}
