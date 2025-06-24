using System.Collections.Generic;

namespace BNKaraoke.DJ.Models;

public class LoginResult
{
    public string? Token { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public List<string>? Roles { get; set; }
}