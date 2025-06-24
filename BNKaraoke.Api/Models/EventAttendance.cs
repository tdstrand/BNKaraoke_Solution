namespace BNKaraoke.Api.Models
{
    public class EventAttendance
    {
        public int AttendanceId { get; set; }
        public int EventId { get; set; }
        public string RequestorId { get; set; } = string.Empty; // Matches AspNetUsers.Id (text), renamed from SingerId
        public bool IsCheckedIn { get; set; } = false;
        public bool IsOnBreak { get; set; } = false;
        public DateTime? BreakStartAt { get; set; }
        public DateTime? BreakEndAt { get; set; }

        // Navigation properties
        public Event Event { get; set; } = null!;
        public ApplicationUser Requestor { get; set; } = null!; // Renamed from Singer
        public List<EventAttendanceHistory> AttendanceHistories { get; set; } = new List<EventAttendanceHistory>();
    }
}