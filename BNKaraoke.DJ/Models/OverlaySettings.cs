using System;

namespace BNKaraoke.DJ.Models
{
    public class OverlaySettings
    {
        public bool EnabledTop { get; set; } = true;
        public bool EnabledBottom { get; set; } = true;
        public double TopHeightPercent { get; set; } = 0.10;
        public double BottomHeightPercent { get; set; } = 0.10;
        public double BackgroundOpacity { get; set; } = 0.25;
        public bool UseGradient { get; set; } = true;
        public string PrimaryColor { get; set; } = "#1e3a8a";
        public string SecondaryColor { get; set; } = "#3b82f6";
        public string Brand { get; set; } = "BNKaraoke.com";
        public OverlayTemplates Templates { get; set; } = new OverlayTemplates();

        public void Clamp()
        {
            TopHeightPercent = ClampPercent(TopHeightPercent);
            BottomHeightPercent = ClampPercent(BottomHeightPercent);
            BackgroundOpacity = Math.Clamp(BackgroundOpacity, 0.0, 1.0);
            Templates ??= new OverlayTemplates();
            Templates.EnsureDefaults();
        }

        private static double ClampPercent(double value)
        {
            return double.IsFinite(value) ? Math.Clamp(value, 0.0, 1.0) : 0.0;
        }
    }
}
