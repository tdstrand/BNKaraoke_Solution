using System;
using System.Collections.Generic;

namespace BNKaraoke.Api.Models
{
    public class Event
    {
        public int EventId { get; set; }
        public string EventCode { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = "Upcoming";
        public string Visibility { get; set; } = "Visible";
        public string Location { get; set; } = string.Empty;
        public DateTime ScheduledDate { get; set; }
        public TimeSpan? ScheduledStartTime { get; set; }
        public TimeSpan? ScheduledEndTime { get; set; }
        public string? KaraokeDJName { get; set; }
        public bool IsCanceled { get; set; }
        public int RequestLimit { get; set; } = 15;
        public int SongsCompleted { get; set; } = 0; // Added
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Navigation properties
        public List<EventQueue> EventQueues { get; set; } = new List<EventQueue>();
        public List<EventAttendance> EventAttendances { get; set; } = new List<EventAttendance>();
    }
}