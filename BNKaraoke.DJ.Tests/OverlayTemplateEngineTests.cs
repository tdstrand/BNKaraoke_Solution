using System;
using BNKaraoke.DJ.Services.Overlay;
using Xunit;

namespace BNKaraoke.DJ.Tests
{
    public class OverlayTemplateEngineTests
    {
        [Fact]
        public void Render_ReplacesTokensWithValues()
        {
            var engine = new OverlayTemplateEngine();
            var context = new OverlayTemplateContext
            {
                Brand = "BNK",
                Requestor = "Alice",
                Song = "Song",
                Artist = "Artist",
                UpNextRequestor = "Bob",
                UpNextSong = "Tune",
                UpNextArtist = "Singer",
                EventName = "Party",
                Venue = "Club",
                Timestamp = new DateTimeOffset(2024, 1, 1, 20, 15, 0, TimeSpan.Zero)
            };

            var template = "{Brand} - {Requestor} - {Song} - {Artist} - {UpNextRequestor} - {UpNextSong} - {UpNextArtist} - {EventName} - {Venue} - {Time}";
            var result = engine.Render(template, context);

            Assert.Equal("BNK - Alice - Song - Artist - Bob - Tune - Singer - Party - Club - 8:15 PM", result);
        }

        [Fact]
        public void Render_HandlesMissingValues()
        {
            var engine = new OverlayTemplateEngine();
            var context = new OverlayTemplateContext();

            var result = engine.Render("Play {Requestor}  {Song}", context);

            Assert.Equal("Play", result);
        }

        [Fact]
        public void Render_CollapsesWhitespaceAfterReplacement()
        {
            var engine = new OverlayTemplateEngine();
            var context = new OverlayTemplateContext
            {
                Requestor = "  Alice  ",
                Song = string.Empty
            };

            var template = "Now playing   {Requestor}   {Song}";

            var result = engine.Render(template, context);

            Assert.Equal("Now playing Alice", result);
        }

        [Fact]
        public void Render_UsesTimestampForTimeToken()
        {
            var engine = new OverlayTemplateEngine();
            var context = new OverlayTemplateContext
            {
                Timestamp = new DateTimeOffset(2024, 6, 1, 21, 30, 0, TimeSpan.Zero)
            };

            var result = engine.Render("It is {Time}", context);

            Assert.Equal("It is 9:30 PM", result);
        }
    }
}
