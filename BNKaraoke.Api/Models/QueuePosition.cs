using System;

namespace BNKaraoke.Api.Models
{
    public class QueuePosition
    {
        public required string QueueId { get; set; }
        public required string SingerName { get; set; }
        public int Score { get; set; }
        public required string Reason { get; set; }
        public bool IsVip { get; set; }
        public bool IsOffline { get; set; }
        public bool OnHold { get; set; }
        public required string RequestorUserName { get; set; }
        public required string SongId { get; set; }
        public required int Position { get; set; }
        public required string UserId { get; set; }
        public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    }
}
