using System.Collections.Generic;

namespace BNKaraoke.DJ.Models;

public class UserInfo
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? PhoneNumber { get; set; }
    public List<string>? Roles { get; set; }
}