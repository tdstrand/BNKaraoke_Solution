namespace BNKaraoke.Api.DTOs;

public class RegisterDto
{
    public required string PhoneNumber { get; set; }
    public required string Password { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string[] Roles { get; set; }
    public string? PinCode { get; set; } // Added for PIN code validation
    public bool? MustChangePassword { get; set; } // Added to set MustChangePassword flag
}