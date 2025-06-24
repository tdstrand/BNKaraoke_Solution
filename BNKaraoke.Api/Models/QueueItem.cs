namespace BNKaraoke.Api.Models
{
    public class QueueItem
    {
        public int Id { get; set; }
        public int EventId { get; set; }
        public int SongId { get; set; }
        public string[] Singers { get; set; } = Array.Empty<string>();
        public string[] Requests { get; set; } = Array.Empty<string>(); // e.g., ["For Sarah: Pending"]
    }
}