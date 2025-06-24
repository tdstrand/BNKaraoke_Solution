namespace BNKaraoke.Api.Dtos
{
    public class DJSingerDto
    {
        public string? UserId { get; set; }
        public string? DisplayName { get; set; }
        public bool IsLoggedIn { get; set; }
        public bool IsJoined { get; set; }
        public bool IsOnBreak { get; set; }
    }
}