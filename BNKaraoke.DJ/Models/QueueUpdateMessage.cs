using System;

namespace BNKaraoke.DJ.Models
{
    public class QueueUpdateMessage
    {
        public int QueueId { get; set; }
        public int EventId { get; set; }
        public string Action { get; set; } = string.Empty;
        public string? YouTubeUrl { get; set; }
        public string? HoldReason { get; set; }
    }
}
