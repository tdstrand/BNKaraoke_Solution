using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BNKaraoke.Api.Models;

public class ApplicationUser : IdentityUser
{
    // Shadow the PhoneNumber property to make it non-nullable
    [Required]
    [Phone]
    public new string PhoneNumber
    {
        get => base.PhoneNumber ?? string.Empty; // Safe because base.PhoneNumber is NOT NULL in DB
        set => base.PhoneNumber = value;
    }

    [Required]
    [Column(TypeName = "nvarchar(100)")]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [Column(TypeName = "nvarchar(100)")]
    public string LastName { get; set; } = string.Empty;

    [Column(TypeName = "boolean")]
    public bool MustChangePassword { get; set; } = false;

    [Column(TypeName = "timestamp with time zone")]
    public DateTime? LastActivity { get; set; } // Added for authentication tracking
}