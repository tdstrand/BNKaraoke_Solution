namespace BNKaraoke.Api.Models
{
    public class EventQueue
    {
        public int QueueId { get; set; }
        public int EventId { get; set; }
        public int SongId { get; set; }
        public required string RequestorUserName { get; set; }
        public required string Singers { get; set; }
        public int Position { get; set; }
        public required string Status { get; set; }
        public bool IsActive { get; set; }
        public bool WasSkipped { get; set; }
        public bool IsCurrentlyPlaying { get; set; }
        public DateTime? SungAt { get; set; }
        public bool IsOnBreak { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public Event? Event { get; set; }
        public Song? Song { get; set; }
        public ApplicationUser? Requestor { get; set; }
    }
}