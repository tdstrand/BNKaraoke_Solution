#nullable enable
using System;

namespace BNKaraoke.DJ.Services;

public class AppState
{
    public string? JoinedEventName { get; set; }
    public Guid? JoinedEventId { get; set; }
    public bool IsLoggedIn { get; set; }
    public string? DJFirstName { get; set; }
    public string? DJLastName { get; set; }
    public string? Token { get; set; }
}
