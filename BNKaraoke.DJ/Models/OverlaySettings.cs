using System;

namespace BNKaraoke.DJ.Models
{
    public class OverlaySettings
    {
        private const double DefaultMarqueeSpeed = 90.0;
        private const double DefaultSpacerWidth = 140.0;
        private const int DefaultCrossfadeMs = 200;

        public bool EnabledTop { get; set; } = true;
        public bool EnabledBottom { get; set; } = true;
        public double TopHeightPercent { get; set; } = 0.10;
        public double BottomHeightPercent { get; set; } = 0.10;
        public double BackgroundOpacity { get; set; } = 0.25;
        public bool UseGradient { get; set; } = true;
        public string PrimaryColor { get; set; } = "#1e3a8a";
        public string SecondaryColor { get; set; } = "#3b82f6";
        public string Brand { get; set; } = "BNKaraoke.com";
        public bool MarqueeEnabled { get; set; } = true;
        public double MarqueeSpeedPxPerSecond { get; set; } = DefaultMarqueeSpeed;
        public double MarqueeSpacerWidthPx { get; set; } = DefaultSpacerWidth;
        public int MarqueeCrossfadeMs { get; set; } = DefaultCrossfadeMs;
        public OverlayTemplates Templates { get; set; } = new OverlayTemplates();

        public void Clamp()
        {
            TopHeightPercent = ClampPercent(TopHeightPercent);
            BottomHeightPercent = ClampPercent(BottomHeightPercent);
            BackgroundOpacity = Math.Clamp(BackgroundOpacity, 0.0, 1.0);
            Templates ??= new OverlayTemplates();
            Templates.EnsureDefaults();
            MarqueeSpeedPxPerSecond = ClampSpeed(MarqueeSpeedPxPerSecond);
            MarqueeSpacerWidthPx = ClampSpacer(MarqueeSpacerWidthPx);
            MarqueeCrossfadeMs = ClampCrossfade(MarqueeCrossfadeMs);
        }

        private static double ClampPercent(double value)
        {
            return double.IsFinite(value) ? Math.Clamp(value, 0.0, 1.0) : 0.0;
        }

        private static double ClampSpeed(double value)
        {
            if (!double.IsFinite(value))
            {
                return DefaultMarqueeSpeed;
            }

            return Math.Clamp(value, 10.0, 500.0);
        }

        private static double ClampSpacer(double value)
        {
            if (!double.IsFinite(value))
            {
                return DefaultSpacerWidth;
            }

            return Math.Clamp(value, 20.0, 400.0);
        }

        private static int ClampCrossfade(int value)
        {
            return Math.Clamp(value, 0, 5000);
        }
    }
}
