namespace BNKaraoke.Api.Models;
public class Singer
{
    public string UserId { get; set; } = string.Empty; // Matches ApplicationUser.Id
    public int EventId { get; set; } // Matches Event.EventId
    public string DisplayName { get; set; } = string.Empty; // FirstName + LastName
    public bool IsLoggedIn { get; set; }
    public bool IsJoined { get; set; } // Maps to EventAttendance.IsCheckedIn
    public bool IsOnBreak { get; set; } // Maps to EventAttendance.IsOnBreak

    // Navigation properties
    public Event? Event { get; set; }
    public ApplicationUser? User { get; set; }
}