// Add to BNKaraoke.DJ\Models\EventQueueDto.cs (new file)
using System;
using System.Collections.Generic;

namespace BNKaraoke.DJ.Models
{
    public class EventQueueDto
    {
        public int QueueId { get; set; }
        public int EventId { get; set; }
        public int SongId { get; set; }
        public string? SongTitle { get; set; }
        public string? SongArtist { get; set; }
        public string? YouTubeUrl { get; set; }
        public required string RequestorUserName { get; set; }
        public string? RequestorFullName { get; set; }
        public List<string> Singers { get; set; } = new List<string>();
        public int Position { get; set; }
        public required string Status { get; set; }
        public bool IsActive { get; set; }
        public bool WasSkipped { get; set; }
        public bool IsCurrentlyPlaying { get; set; }
        public DateTime? SungAt { get; set; }
        public bool IsOnBreak { get; set; }
        public string HoldReason { get; set; } = string.Empty;
        public bool IsUpNext { get; set; }
        public bool IsSingerLoggedIn { get; set; }
        public bool IsSingerJoined { get; set; }
        public bool IsSingerOnBreak { get; set; }
        public bool IsServerCached { get; set; }
        public bool IsMature { get; set; }
        public float? NormalizationGain { get; set; }
        public float? FadeStartTime { get; set; }
        public float? IntroMuteDuration { get; set; }
    }
}