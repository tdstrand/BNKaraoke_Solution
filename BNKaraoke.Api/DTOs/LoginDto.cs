using System.ComponentModel.DataAnnotations;

namespace BNKaraoke.Api.DTOs
{
    public class LoginDto
    {
        [Required]
        public required string UserName { get; set; }

        [Required]
        [DataType(DataType.Password)]
        public required string Password { get; set; }
    }

    public class UserResponseDto
    {
        public required string Token { get; set; }
        public required string FirstName { get; set; }
        public required string LastName { get; set; }
        public required List<string> Roles { get; set; }
    }
}