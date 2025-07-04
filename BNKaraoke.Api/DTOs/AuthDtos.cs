namespace BNKaraoke.Api.DTOs
{
    public class AssignRolesDto
    {
        public required string UserId { get; set; }
        public required string[] Roles { get; set; }
    }

    public class DeleteUserDto
    {
        public required string UserId { get; set; }
    }

    public class ResetPasswordDto
    {
        public required string UserId { get; set; }
        public required string NewPassword { get; set; }
    }

    public class UpdateUserDto
    {
        public required string UserId { get; set; }
        public required string UserName { get; set; }
        public string? Password { get; set; }
        public required string FirstName { get; set; }
        public required string LastName { get; set; }
        public required string[] Roles { get; set; }
    }

    public class AddUserDto
    {
        public required string PhoneNumber { get; set; }
        public required string FirstName { get; set; }
        public required string LastName { get; set; }
    }

    public class ForcePasswordChangeDto
    {
        public required bool MustChangePassword { get; set; }
    }

    public class ChangePasswordDto
    {
        public string? CurrentPassword { get; set; }
        public required string NewPassword { get; set; }
    }

    public class UpdateRegistrationSettingsDto
    {
        public required string PinCode { get; set; }
    }
}