namespace BNKaraoke.DJ.Models
{
    public class AppSettings
    {
        public string? ApiBaseUrl { get; set; }
        public HydrationSettings Hydration { get; set; } = new();
    }
}
