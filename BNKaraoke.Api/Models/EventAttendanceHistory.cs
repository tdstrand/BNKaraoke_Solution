namespace BNKaraoke.Api.Models
{
    public class EventAttendanceHistory
    {
        public int HistoryId { get; set; }
        public int EventId { get; set; }
        public string RequestorId { get; set; } = string.Empty; // Matches AspNetUsers.Id (text), renamed from SingerId
        public string Action { get; set; } = string.Empty; // CheckIn, CheckOut
        public DateTime ActionTimestamp { get; set; }
        public int? AttendanceId { get; set; }

        // Navigation properties
        public Event Event { get; set; } = null!;
        public ApplicationUser Requestor { get; set; } = null!; // Renamed from Singer
        public EventAttendance? Attendance { get; set; }
    }
}