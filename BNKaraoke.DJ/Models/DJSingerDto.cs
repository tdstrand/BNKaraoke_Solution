namespace BNKaraoke.DJ.Models
{
    public class DJSingerDto
    {
        public string UserId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool IsLoggedIn { get; set; }
        public bool IsJoined { get; set; }
        public bool IsOnBreak { get; set; }
    }
}