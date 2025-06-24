using System;

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
        public TimeSpan? ScheduledStartTime { get; set; }
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
        public required string RequestorUserName { get; set; }
        public required List<string> Singers { get; set; }
        public int Position { get; set; }
        public required string Status { get; set; }
        public bool IsActive { get; set; }
        public bool WasSkipped { get; set; }
        public bool IsCurrentlyPlaying { get; set; }
        public DateTime? SungAt { get; set; }
        public bool IsOnBreak { get; set; }
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
        public TimeSpan? ScheduledStartTime { get; set; }
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
        public TimeSpan? ScheduledStartTime { get; set; }
        public TimeSpan? ScheduledEndTime { get; set; }
        public string? KaraokeDJName { get; set; }
        public bool? IsCanceled { get; set; }
        public int RequestLimit { get; set; } = 15;
    }
}