using System;
using System.Collections.Generic;

namespace BNKaraoke.Api.Dtos
{
    // Bit flags in case we add more states later (Muted, VIP, etc.)
    [System.Flags]
    public enum SingerStatusFlags
    {
        None     = 0,
        LoggedIn = 1 << 0,
        Joined   = 1 << 1,
        OnBreak  = 1 << 2,
        // Reserved for future: Muted = 1 << 3, ...
    }

    public class SingerStatusDto
    {
        public string UserId { get; set; } = string.Empty;      // username/phone
        public string DisplayName { get; set; } = string.Empty;  // FirstName LastName
        public bool IsLoggedIn { get; set; }
        public bool IsJoined { get; set; }
        public bool IsOnBreak { get; set; }
        public SingerStatusFlags Flags { get; set; }             // convenience bit flags
    }

    /// <summary>
    /// Unified queue item for DJs — always includes singer status.
    /// Supports future multi-singer items via Singers[].
    /// </summary>
    public class DJQueueItemDto
    {
        // Identity
        public int QueueId { get; set; }
        public int EventId { get; set; }

        // Song
        public int SongId { get; set; }
        public string SongTitle { get; set; } = string.Empty;
        public string SongArtist { get; set; } = string.Empty;
        public string? YouTubeUrl { get; set; }

        // Requestor + future multi-singer
        public string RequestorUserName { get; set; } = string.Empty;
        public string? RequestorDisplayName { get; set; }
        public List<string> Singers { get; set; } = new(); // future: array of singers
        public SingerStatusDto Singer { get; set; } = new(); // unified status for primary/requestor

        // Queue state
        public int Position { get; set; }
        public string Status { get; set; } = string.Empty; // Active, Skipped, Sung, etc.
        public bool IsActive { get; set; }
        public bool WasSkipped { get; set; }
        public bool IsCurrentlyPlaying { get; set; }
        public DateTimeOffset? SungAt { get; set; }
        public bool IsOnBreak { get; set; }
        public bool IsUpNext { get; set; }
        public string HoldReason { get; set; } = string.Empty; // e.g., NotJoined, OnBreak

        // Singer convenience mirrors (redundant but handy for clients)
        public bool IsSingerLoggedIn { get; set; }
        public bool IsSingerJoined { get; set; }
        public bool IsSingerOnBreak { get; set; }

        // Media / quality
        public bool IsServerCached { get; set; }
        public bool IsMature { get; set; }
        public double? NormalizationGain { get; set; }
        public TimeSpan? FadeStartTime { get; set; }
        public TimeSpan? IntroMuteDuration { get; set; }
    }
}
