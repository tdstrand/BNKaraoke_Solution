using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BNKaraoke.DJ.Services.Overlay
{
    public class OverlayTemplateContext
    {
        public string Brand { get; set; } = string.Empty;
        public string Requestor { get; set; } = string.Empty;
        public string Song { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string UpNextRequestor { get; set; } = string.Empty;
        public string UpNextSong { get; set; } = string.Empty;
        public string UpNextArtist { get; set; } = string.Empty;
        public string EventName { get; set; } = string.Empty;
        public string Venue { get; set; } = string.Empty;
        public DateTimeOffset? Timestamp { get; set; }
        internal string GetValue(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return string.Empty;
            }

            return token.ToLowerInvariant() switch
            {
                "brand" => Brand ?? string.Empty,
                "requestor" => Requestor ?? string.Empty,
                "song" => Song ?? string.Empty,
                "artist" => Artist ?? string.Empty,
                "upnextrequestor" => UpNextRequestor ?? string.Empty,
                "upnextsong" => UpNextSong ?? string.Empty,
                "upnextartist" => UpNextArtist ?? string.Empty,
                "eventname" => EventName ?? string.Empty,
                "venue" => Venue ?? string.Empty,
                "time" => FormatTime(Timestamp),
                _ => string.Empty,
            };
        }

        private static string FormatTime(DateTimeOffset? timestamp)
        {
            var value = timestamp ?? DateTimeOffset.Now;
            return value.ToString("h:mm tt", CultureInfo.InvariantCulture);
        }
    }

    public class OverlayTemplateEngine
    {
        private static readonly Regex TokenRegex = new(@"\{(?<token>[A-Za-z]+)\}", RegexOptions.Compiled);
        private static readonly Regex MultiSpaceRegex = new(@"\s{2,}", RegexOptions.Compiled);

        public string Render(string template, OverlayTemplateContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (string.IsNullOrWhiteSpace(template))
            {
                return string.Empty;
            }

            var replaced = TokenRegex.Replace(template, match =>
            {
                var token = match.Groups["token"].Value;
                return context.GetValue(token);
            });

            replaced = MultiSpaceRegex.Replace(replaced, " ");
            return replaced.Trim();
        }
    }
}
