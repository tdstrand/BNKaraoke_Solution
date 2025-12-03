using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace BNKaraoke.Api.Dtos
{
    public class EventDto
    {
        public int EventId { get; set; }
        public string EventCode { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Visibility { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public DateTime ScheduledDate { get; set; }
        [JsonConverter(typeof(CustomTimeSpanConverter))]
        public TimeSpan? ScheduledStartTime { get; set; }
        [JsonConverter(typeof(CustomTimeSpanConverter))]
        public TimeSpan? ScheduledEndTime { get; set; }
        public string? KaraokeDJName { get; set; }
        public bool IsCanceled { get; set; }
        public int RequestLimit { get; set; }
        public int QueueCount { get; set; }
        public int SongsCompleted { get; set; }
    }

    public class EventQueueDto
    {
        public int QueueId { get; set; }
        public int EventId { get; set; }
        public int SongId { get; set; }
        public string? SongTitle { get; set; }
        public string? SongArtist { get; set; }
        public string? YouTubeUrl { get; set; }
        public required string RequestorUserName { get; set; }
        public string? RequestorFullName { get; set; }
        public List<string> Singers { get; set; } = new List<string>();
        public int Position { get; set; }
        public required string Status { get; set; }
        public bool IsActive { get; set; }
        public bool WasSkipped { get; set; }
        public bool IsCurrentlyPlaying { get; set; }
        public DateTime? SungAt { get; set; }
        public bool IsOnBreak { get; set; }
        public string HoldReason { get; set; } = string.Empty;
        public bool IsUpNext { get; set; }
        public bool IsSingerLoggedIn { get; set; }
        public bool IsSingerJoined { get; set; }
        public bool IsSingerOnBreak { get; set; }
        public bool IsServerCached { get; set; }
        public bool IsMature { get; set; }
        public float? NormalizationGain { get; set; }
        public float? FadeStartTime { get; set; }
        public float? IntroMuteDuration { get; set; }
    }

    public class EventQueueCreateDto
    {
        public int SongId { get; set; }
        public string RequestorUserName { get; set; } = string.Empty;
    }

    public class AttendanceActionDto
    {
        public string RequestorId { get; set; } = string.Empty;
    }

    public class ReorderQueueRequest
    {
        public List<QueuePosition> NewOrder { get; set; } = new List<QueuePosition>();
    }

    public class QueuePosition
    {
        public int QueueId { get; set; }
        public int Position { get; set; }
    }

    public class UpdateQueueSingersDto
    {
        public List<string>? Singers { get; set; }
    }

    public class EventCreateDto
    {
        public string EventCode { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? Status { get; set; }
        public string? Visibility { get; set; }
        public string Location { get; set; } = string.Empty;
        public DateTime ScheduledDate { get; set; }
        [JsonConverter(typeof(CustomTimeSpanConverter))]
        public TimeSpan? ScheduledStartTime { get; set; }
        [JsonConverter(typeof(CustomTimeSpanConverter))]
        public TimeSpan? ScheduledEndTime { get; set; }
        public string? KaraokeDJName { get; set; }
        public bool? IsCanceled { get; set; }
        public int RequestLimit { get; set; } = 15;
    }

    public class EventUpdateDto
    {
        public string EventCode { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? Status { get; set; }
        public string? Visibility { get; set; }
        public string Location { get; set; } = string.Empty;
        public DateTime ScheduledDate { get; set; }
        [JsonConverter(typeof(CustomTimeSpanConverter))]
        public TimeSpan? ScheduledStartTime { get; set; }
        [JsonConverter(typeof(CustomTimeSpanConverter))]
        public TimeSpan? ScheduledEndTime { get; set; }
        public string? KaraokeDJName { get; set; }
        public bool? IsCanceled { get; set; }
        public int RequestLimit { get; set; } = 15;
    }

    public class CustomTimeSpanConverter : JsonConverter<TimeSpan?>
    {
        private static readonly Regex MalformedTimeSpanRegex = new Regex(@"^(\d{1,2})\.(\d{2}(?::\d{2})?(?::\d{2})?)$", RegexOptions.Compiled);
        private static readonly Regex ExtendedTimeSpanRegex = new Regex(@"^(\d{1,2}):(\d{2}):(\d{2})$", RegexOptions.Compiled);

        public override TimeSpan? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            var value = reader.GetString();
            if (string.IsNullOrEmpty(value))
                return null;

            // Log the raw value for debugging
            Console.WriteLine($"CustomTimeSpanConverter: Attempting to parse TimeSpan: {value}");

            // Handle malformed formats like "HH.mm:ss:ff" or "HH.mm:ss" (e.g., "1.03:30:00")
            if (value.Contains('.'))
            {
                var match = MalformedTimeSpanRegex.Match(value);
                if (match.Success)
                {
                    // Convert HH.mm:ss:ff or HH.mm:ss to HH:mm:ss
                    value = $"{match.Groups[1].Value}:{match.Groups[2].Value}";
                    if (value.EndsWith(":00", StringComparison.OrdinalIgnoreCase))
                        value = value.Substring(0, value.Length - 3);
                    Console.WriteLine($"CustomTimeSpanConverter: Normalized malformed value to: {value}");
                }
            }

            TimeSpan timeSpan;

            // Handle extended HH:mm:ss format (e.g., "27:30:00" or "1:03:30")
            var extendedMatch = ExtendedTimeSpanRegex.Match(value);
            if (extendedMatch.Success)
            {
                if (int.TryParse(extendedMatch.Groups[1].Value, out var hours) &&
                    int.TryParse(extendedMatch.Groups[2].Value, out var minutes) &&
                    int.TryParse(extendedMatch.Groups[3].Value, out var seconds))
                {
                    if (hours >= 0 && hours <= 47 && minutes >= 0 && minutes < 60 && seconds >= 0 && seconds < 60)
                    {
                        timeSpan = new TimeSpan(hours, minutes, seconds);
                        Console.WriteLine($"CustomTimeSpanConverter: Successfully parsed {value} as extended HH:mm:ss");
                        return timeSpan;
                    }
                    Console.WriteLine($"CustomTimeSpanConverter: Invalid values in {value}: hours={hours}, minutes={minutes}, seconds={seconds}");
                }
            }

            // Try parsing HH:mm:ss format (for hours < 24)
            if (TimeSpan.TryParseExact(value, "hh\\:mm\\:ss", null, out timeSpan))
            {
                Console.WriteLine($"CustomTimeSpanConverter: Successfully parsed {value} as HH:mm:ss");
                return timeSpan;
            }

            // Try parsing HH:mm format
            if (TimeSpan.TryParseExact(value, "hh\\:mm", null, out timeSpan))
            {
                Console.WriteLine($"CustomTimeSpanConverter: Successfully parsed {value} as HH:mm");
                return timeSpan;
            }

            // Try parsing ISO 8601 duration (e.g., PT26H, PT26H30M, PT26H30M30S)
            if (TimeSpan.TryParseExact(value, @"PT\dh\hm\s", null, out timeSpan) ||
                TimeSpan.TryParseExact(value, @"PT\dh\hm", null, out timeSpan) ||
                TimeSpan.TryParseExact(value, @"PT\dh", null, out timeSpan))
            {
                Console.WriteLine($"CustomTimeSpanConverter: Successfully parsed {value} as ISO 8601 duration");
                return timeSpan;
            }

            throw new JsonException($"Invalid TimeSpan format: {value}. Expected formats: 'HH:mm:ss' (hours up to 47), 'HH:mm', or 'PT<Hours>H[<Minutes>M[<Seconds>S]]'.");
        }

        public override void Write(Utf8JsonWriter writer, TimeSpan? value, JsonSerializerOptions options)
        {
            if (value.HasValue)
                writer.WriteStringValue(value.Value.ToString(@"hh\:mm\:ss"));
            else
                writer.WriteNullValue();
        }
    }
}
