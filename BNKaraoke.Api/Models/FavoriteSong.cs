namespace BNKaraoke.Api.Models
{
    public class FavoriteSong
    {
        public int Id { get; set; }
        public string SingerId { get; set; } = string.Empty;
        public int SongId { get; set; }
    }
}