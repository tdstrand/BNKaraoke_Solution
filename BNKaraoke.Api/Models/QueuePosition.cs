using System;

namespace BNKaraoke.Api.Models
{
    public class QueuePosition
    {
        public string QueueId { get; set; }
        public string SingerName { get; set; }
        public int CurrentPosition { get; set; }
        public int SuggestedPosition { get; set; }
        public double Score { get; set; }
        public string Reason { get; set; }
        public DateTime AddedAt { get; set; }
        public bool IsOnHold { get; set; }
        public bool IsSingerLoggedIn { get; set; }
        public bool IsSingerJoined { get; set; }
        public string RequestorUserName { get; set; }
    }
}
