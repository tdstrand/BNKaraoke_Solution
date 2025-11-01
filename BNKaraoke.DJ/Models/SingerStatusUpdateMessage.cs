using System;

namespace BNKaraoke.DJ.Models
{
    public class SingerStatusUpdateMessage
    {
        public string UserId { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public bool IsLoggedIn { get; set; }
        public bool IsJoined { get; set; }
        public bool IsOnBreak { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
        public Guid? UpdateId { get; set; }
    }
}
