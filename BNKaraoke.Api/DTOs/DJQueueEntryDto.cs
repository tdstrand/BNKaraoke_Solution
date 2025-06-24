using System;
using System.Collections.Generic;

namespace BNKaraoke.Api.Dtos
{
    public class DJQueueEntryDto
    {
        public int QueueId { get; set; }
        public int EventId { get; set; }
        public int SongId { get; set; }
        public string? SongTitle { get; set; }
        public string? SongArtist { get; set; }
        public string? RequestorDisplayName { get; set; }
        public string VideoLength { get; set; } = string.Empty;
        public int Position { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? RequestorUserName { get; set; }
        public List<string> Singers { get; set; } = new List<string>();
        public bool IsActive { get; set; }
        public bool WasSkipped { get; set; }
        public bool IsCurrentlyPlaying { get; set; }
        public DateTime? SungAt { get; set; }
        public string? Genre { get; set; }
        public string? Decade { get; set; }
        public string? YouTubeUrl { get; set; }
        public bool IsVideoCached { get; set; }
        public bool IsOnBreak { get; set; }
        public int SongsCompleted { get; set; } // Added
    }
}