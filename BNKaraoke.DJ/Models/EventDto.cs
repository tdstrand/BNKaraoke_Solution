using System;

namespace BNKaraoke.DJ.Models;

public class EventDto
{
    public int EventId { get; set; }
    public string? EventCode { get; set; }
    public string? Description { get; set; }
    public string? Status { get; set; }
    public string? Visibility { get; set; }
    public string? Location { get; set; }
    public DateTime ScheduledDate { get; set; }
    public TimeSpan? ScheduledStartTime { get; set; }
    public TimeSpan? ScheduledEndTime { get; set; }
    public string? KaraokeDJName { get; set; }
    public bool IsCanceled { get; set; }
    public int RequestLimit { get; set; }
    public int QueueCount { get; set; }
}