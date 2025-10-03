using System.Text.Json;
using BNKaraoke.DJ.Models;
using Xunit;

namespace BNKaraoke.DJ.Tests
{
    public class OverlaySettingsPersistenceTests
    {
        [Fact]
        public void SaveAndLoad_RoundTripsOverlaySettings()
        {
            var settings = new OverlaySettings
            {
                EnabledTop = false,
                EnabledBottom = true,
                TopHeightPercent = 0.25,
                BottomHeightPercent = 0.15,
                BackgroundOpacity = 0.55,
                UseGradient = false,
                PrimaryColor = "#112233",
                SecondaryColor = "#445566",
                Brand = "BNK",
                FontFamily = "Arial",
                FontSize = 52,
                FontWeight = "SemiBold",
                FontColor = "#FF00FF00",
                FontStrokeEnabled = false,
                FontShadowEnabled = false,
                MarqueeEnabled = false,
                MarqueeSpeedPxPerSecond = 140,
                MarqueeSpacerWidthPx = 180,
                MarqueeCrossfadeMs = 400,
                Templates = new OverlayTemplates
                {
                    PlaybackTop = "Top {Requestor}",
                    PlaybackBottom = "Bottom {Song}",
                    BlueTop = "Blue Top",
                    BlueBottom = "Blue Bottom"
                }
            };

            settings.Clamp();
            var container = new DjSettings { Overlay = settings };

            var json = JsonSerializer.Serialize(container);
            var reloaded = JsonSerializer.Deserialize<DjSettings>(json);

            Assert.NotNull(reloaded);
            var overlay = reloaded!.Overlay;
            Assert.NotNull(overlay);

            overlay!.Clamp();

            Assert.Equal(settings.EnabledTop, overlay.EnabledTop);
            Assert.Equal(settings.EnabledBottom, overlay.EnabledBottom);
            Assert.Equal(settings.TopHeightPercent, overlay.TopHeightPercent);
            Assert.Equal(settings.BottomHeightPercent, overlay.BottomHeightPercent);
            Assert.Equal(settings.BackgroundOpacity, overlay.BackgroundOpacity);
            Assert.Equal(settings.UseGradient, overlay.UseGradient);
            Assert.Equal(settings.PrimaryColor, overlay.PrimaryColor);
            Assert.Equal(settings.SecondaryColor, overlay.SecondaryColor);
            Assert.Equal(settings.Brand, overlay.Brand);
            Assert.Equal(settings.FontFamily, overlay.FontFamily);
            Assert.Equal(settings.FontSize, overlay.FontSize);
            Assert.Equal(settings.FontWeight, overlay.FontWeight);
            Assert.Equal(settings.FontColor, overlay.FontColor);
            Assert.Equal(settings.FontStrokeEnabled, overlay.FontStrokeEnabled);
            Assert.Equal(settings.FontShadowEnabled, overlay.FontShadowEnabled);
            Assert.Equal(settings.MarqueeEnabled, overlay.MarqueeEnabled);
            Assert.Equal(settings.MarqueeSpeedPxPerSecond, overlay.MarqueeSpeedPxPerSecond);
            Assert.Equal(settings.MarqueeSpacerWidthPx, overlay.MarqueeSpacerWidthPx);
            Assert.Equal(settings.MarqueeCrossfadeMs, overlay.MarqueeCrossfadeMs);
            Assert.Equal(settings.Templates.PlaybackTop, overlay.Templates.PlaybackTop);
            Assert.Equal(settings.Templates.PlaybackBottom, overlay.Templates.PlaybackBottom);
            Assert.Equal(settings.Templates.BlueTop, overlay.Templates.BlueTop);
            Assert.Equal(settings.Templates.BlueBottom, overlay.Templates.BlueBottom);
        }

        [Fact]
        public void Clamp_AppliesDefaultsWhenValuesInvalid()
        {
            var settings = new OverlaySettings
            {
                TopHeightPercent = -1.0,
                BottomHeightPercent = double.NaN,
                BackgroundOpacity = 2.0,
                FontFamily = string.Empty,
                FontSize = 500.0,
                FontWeight = string.Empty,
                FontColor = "12345",
                MarqueeSpeedPxPerSecond = double.PositiveInfinity,
                MarqueeSpacerWidthPx = double.NaN,
                MarqueeCrossfadeMs = -10,
                Templates = new OverlayTemplates
                {
                    PlaybackTop = string.Empty,
                    PlaybackBottom = string.Empty,
                    BlueTop = string.Empty,
                    BlueBottom = string.Empty
                }
            };

            settings.Clamp();

            Assert.Equal(0.0, settings.TopHeightPercent);
            Assert.Equal(0.0, settings.BottomHeightPercent);
            Assert.Equal(1.0, settings.BackgroundOpacity);
            Assert.Equal("Segoe UI", settings.FontFamily);
            Assert.Equal("Bold", settings.FontWeight);
            Assert.Equal("#FFFFFFFF", settings.FontColor);
            Assert.Equal(96.0, settings.FontSize);
            Assert.Equal(90.0, settings.MarqueeSpeedPxPerSecond);
            Assert.Equal(140.0, settings.MarqueeSpacerWidthPx);
            Assert.Equal(0, settings.MarqueeCrossfadeMs);
            Assert.False(string.IsNullOrWhiteSpace(settings.Templates.PlaybackTop));
            Assert.False(string.IsNullOrWhiteSpace(settings.Templates.PlaybackBottom));
            Assert.False(string.IsNullOrWhiteSpace(settings.Templates.BlueTop));
            Assert.False(string.IsNullOrWhiteSpace(settings.Templates.BlueBottom));
        }
    }
}
