namespace BNKaraoke.Api.Models
{
    public class KaraokeChannel
    {
        public int Id { get; set; }
        public string ChannelName { get; set; } = string.Empty;
        public string? ChannelId { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
    }
}