using BNKaraoke.Api.Data;
using BNKaraoke.Api.DTOs;
using BNKaraoke.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace BNKaraoke.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthController> _logger;
        private readonly ApplicationDbContext _context;

        public AuthController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            RoleManager<IdentityRole> roleManager,
            IConfiguration configuration,
            ILogger<AuthController> logger,
            ApplicationDbContext context)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _roleManager = roleManager;
            _configuration = configuration;
            _logger = logger;
            _context = context;
            _logger.LogInformation("AuthController instantiated");
        }

        [HttpGet("test")]
        public IActionResult Test()
        {
            _logger.LogInformation("Test endpoint called");
            return Ok("API is working!");
        }

        [HttpGet("version")]
        public IActionResult GetVersion()
        {
            _logger.LogInformation("Version endpoint called");
            return Ok(new { Version = "Post-0987694", CorsEnabled = true });
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto model)
        {
            _logger.LogInformation("Register: Received payload - {Payload}", JsonConvert.SerializeObject(model));
            if (model == null || string.IsNullOrEmpty(model.PhoneNumber))
            {
                _logger.LogWarning("Register: Model or PhoneNumber is null");
                return BadRequest(new { error = "PhoneNumber is required" });
            }

            // Validate PIN code for non-admin registration
            if (!User.IsInRole("User Manager"))
            {
                if (string.IsNullOrEmpty(model.PinCode))
                {
                    _logger.LogWarning("Register: PinCode is required for non-admin registration");
                    return BadRequest(new { error = "PinCode is required" });
                }

                var sw = Stopwatch.StartNew();
                var registrationSettings = await _context.RegistrationSettings.AsNoTracking().FirstOrDefaultAsync();
                _logger.LogInformation("Register: RegistrationSettings query took {ElapsedMilliseconds} ms", sw.ElapsedMilliseconds);
                if (registrationSettings == null || registrationSettings.CurrentPin != model.PinCode)
                {
                    _logger.LogWarning("Register: Invalid PinCode provided");
                    return BadRequest(new { error = "Invalid PIN code" });
                }
            }

            var user = new ApplicationUser
            {
                UserName = model.PhoneNumber,
                PhoneNumber = model.PhoneNumber,
                NormalizedUserName = _userManager.NormalizeName(model.PhoneNumber),
                FirstName = model.FirstName,
                LastName = model.LastName,
                EmailConfirmed = true,
                MustChangePassword = model.MustChangePassword ?? false
            };
            _logger.LogInformation("Register: Creating user - UserName: {UserName}, PhoneNumber: {PhoneNumber}, MustChangePassword: {MustChangePassword}", user.UserName, user.PhoneNumber, user.MustChangePassword);

            var result = await _userManager.CreateAsync(user, model.Password);
            if (!result.Succeeded)
            {
                _logger.LogError("Register: Failed to create user - Errors: {Errors}", string.Join(", ", result.Errors.Select(e => e.Description)));
                return BadRequest(new { error = "Registration failed", details = result.Errors });
            }

            var createdUser = await _userManager.FindByNameAsync(model.PhoneNumber);
            if (createdUser == null)
            {
                _logger.LogError("Register: Failed to find newly created user - UserName: {UserName}", model.PhoneNumber);
                return StatusCode(500, new { error = "Failed to find created user" });
            }
            await _userManager.AddToRolesAsync(createdUser, model.Roles);
            _logger.LogInformation("Register: User {UserName} registered with roles: {Roles}", createdUser.UserName, string.Join(", ", model.Roles));
            return Ok(new { message = "User registered successfully!" });
        }

        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginDto model)
        {
            _logger.LogInformation("Login attempt: UserName={UserName}", model.UserName);

            var sw = Stopwatch.StartNew();
            var user = await _userManager.FindByNameAsync(model.UserName);
            _logger.LogInformation("Login: FindByNameAsync query took {ElapsedMilliseconds} ms", sw.ElapsedMilliseconds);
            if (user == null)
            {
                _logger.LogWarning("Login: User not found - UserName={UserName}", model.UserName);
                return Unauthorized(new { error = "Invalid login attempt." });
            }

            _logger.LogInformation("Login: User found. Checking password...");
            var isPasswordValid = await _signInManager.CheckPasswordSignInAsync(user, model.Password, false);
            if (!isPasswordValid.Succeeded)
            {
                _logger.LogWarning("Login: Invalid password for UserName={UserName}", model.UserName);
                return Unauthorized(new { error = "Invalid credentials." });
            }

            _logger.LogInformation("Login: Password validated. Updating LastActivity...");
            const int maxRetries = 3;
            for (int retry = 0; retry < maxRetries; retry++)
            {
                user.LastActivity = DateTime.UtcNow;
                var updateResult = await _userManager.UpdateAsync(user);
                if (updateResult.Succeeded)
                {
                    break;
                }
                else if (updateResult.Errors.Any(e => e.Code == "ConcurrencyFailure"))
                {
                    if (retry < maxRetries - 1)
                    {
                        _logger.LogWarning("Concurrency failure on attempt {Retry}. Reloading user and retrying...", retry + 1);
                        await _context.Entry(user).ReloadAsync();
                    }
                    else
                    {
                        _logger.LogError("Failed to update LastActivity after {MaxRetries} attempts due to concurrency issues.", maxRetries);
                        return StatusCode(500, new { error = "Failed to update user activity due to persistent concurrency issues." });
                    }
                }
                else
                {
                    _logger.LogError("Login: Failed to update LastActivity for UserName={UserName} - Errors: {Errors}", model.UserName, string.Join(", ", updateResult.Errors.Select(e => e.Description)));
                    return StatusCode(500, new { error = "Failed to update user activity" });
                }
            }

            _logger.LogInformation("Login: Roles retrieved. Generating token...");
            var roles = await _userManager.GetRolesAsync(user);
            var token = GenerateJwtToken(user, roles);

            _logger.LogInformation("Login successful for UserName={UserName} in {TotalElapsedMilliseconds} ms", model.UserName, sw.ElapsedMilliseconds);
            return Ok(new
            {
                message = "Success",
                token,
                userId = user.Id,
                firstName = user.FirstName,
                lastName = user.LastName,
                roles,
                mustChangePassword = user.MustChangePassword
            });
        }

        [HttpGet("users")]
        [Authorize(Roles = "User Manager,Song Manager,Karaoke DJ,Queue Manager,Event Manager,Application Manager")]
        public async Task<IActionResult> GetUsers()
        {
            var sw = Stopwatch.StartNew();
            var users = await _userManager.Users.AsNoTracking().ToListAsync();
            _logger.LogInformation("GetUsers: Users query took {ElapsedMilliseconds} ms", sw.ElapsedMilliseconds);
            var userList = new List<object>();
            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);
                userList.Add(new
                {
                    id = user.Id,
                    userName = user.UserName,
                    displayName = $"{user.FirstName} {user.LastName}".Trim(),
                    email = user.Email ?? "N/A",
                    firstName = user.FirstName,
                    lastName = user.LastName,
                    roles = roles.ToArray(),
                    mustChangePassword = user.MustChangePassword
                });
            }
            _logger.LogInformation("GetUsers: Returning {UserCount} users in {TotalElapsedMilliseconds} ms", userList.Count, sw.ElapsedMilliseconds);
            return Ok(userList);
        }

        [HttpGet("roles")]
        [Authorize(Policy = "User Manager")]
        public IActionResult GetRoles()
        {
            var sw = Stopwatch.StartNew();
            var roles = _roleManager.Roles.AsNoTracking().Select(r => r.Name).ToList();
            _logger.LogInformation("GetRoles: Roles query took {ElapsedMilliseconds} ms, returning {RoleCount} roles", sw.ElapsedMilliseconds, roles.Count);
            return Ok(roles);
        }

        [HttpPost("assign-roles")]
        [Authorize(Policy = "User Manager")]
        public async Task<IActionResult> AssignRoles([FromBody] AssignRolesDto model)
        {
            var sw = Stopwatch.StartNew();
            var user = await _userManager.FindByIdAsync(model.UserId);
            _logger.LogInformation("AssignRoles: FindByIdAsync query took {ElapsedMilliseconds} ms", sw.ElapsedMilliseconds);
            if (user == null)
            {
                _logger.LogWarning("AssignRoles: User not found - UserId={UserId}", model.UserId);
                return NotFound(new { error = "User not found" });
            }

            var currentRoles = await _userManager.GetRolesAsync(user);
            var rolesToRemove = currentRoles.Where(r => !model.Roles.Contains(r)).ToList();
            var rolesToAdd = model.Roles.Where(r => !currentRoles.Contains(r)).ToList();

            var removeResult = await _userManager.RemoveFromRolesAsync(user, rolesToRemove);
            if (!removeResult.Succeeded)
            {
                _logger.LogError("AssignRoles: Failed to remove roles for UserId={UserId} - Errors: {Errors}", model.UserId, string.Join(", ", removeResult.Errors.Select(e => e.Description)));
                return BadRequest(new { error = "Failed to remove roles", details = removeResult.Errors });
            }

            var addResult = await _userManager.AddToRolesAsync(user, rolesToAdd);
            if (!addResult.Succeeded)
            {
                _logger.LogError("AssignRoles: Failed to add roles for UserId={UserId} - Errors: {Errors}", model.UserId, string.Join(", ", addResult.Errors.Select(e => e.Description)));
                return BadRequest(new { error = "Failed to add roles", details = addResult.Errors });
            }

            _logger.LogInformation("AssignRoles: Assigned roles to {UserName}: {Roles} in {TotalElapsedMilliseconds} ms", user.UserName, string.Join(", ", model.Roles), sw.ElapsedMilliseconds);
            return Ok(new { message = "Roles assigned successfully" });
        }

        [HttpPost("delete-user")]
        [Authorize(Policy = "User Manager")]
        public async Task<IActionResult> DeleteUser([FromBody] DeleteUserDto model)
        {
            var sw = Stopwatch.StartNew();
            var user = await _userManager.FindByIdAsync(model.UserId);
            _logger.LogInformation("DeleteUser: FindByIdAsync query took {ElapsedMilliseconds} ms", sw.ElapsedMilliseconds);
            if (user == null)
            {
                _logger.LogWarning("DeleteUser: User not found - UserId={UserId}", model.UserId);
                return NotFound(new { error = "User not found" });
            }

            var result = await _userManager.DeleteAsync(user);
            if (!result.Succeeded)
            {
                _logger.LogError("DeleteUser: Failed to delete user UserId={UserId} - Errors: {Errors}", model.UserId, string.Join(", ", result.Errors.Select(e => e.Description)));
                return BadRequest(new { error = "Failed to delete user", details = result.Errors });
            }

            _logger.LogInformation("DeleteUser: Deleted user: {UserName} in {TotalElapsedMilliseconds} ms", user.UserName, sw.ElapsedMilliseconds);
            return Ok(new { message = "User deleted successfully" });
        }

        [HttpPost("reset-password")]
        [Authorize(Policy = "User Manager")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto model)
        {
            var sw = Stopwatch.StartNew();
            var user = await _userManager.FindByIdAsync(model.UserId);
            _logger.LogInformation("ResetPassword: FindByIdAsync query took {ElapsedMilliseconds} ms", sw.ElapsedMilliseconds);
            if (user == null)
            {
                _logger.LogWarning("ResetPassword: User not found - UserId={UserId}", model.UserId);
                return NotFound(new { error = "User not found" });
            }

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, model.NewPassword);
            if (!result.Succeeded)
            {
                _logger.LogError("ResetPassword: Failed to reset password for UserId={UserId} - Errors: {Errors}", model.UserId, string.Join(", ", result.Errors.Select(e => e.Description)));
                return BadRequest(new { error = "Failed to reset password", details = result.Errors });
            }

            _logger.LogInformation("ResetPassword: Reset password for user: {UserName} in {TotalElapsedMilliseconds} ms", user.UserName, sw.ElapsedMilliseconds);
            return Ok(new { message = "Password reset successfully" });
        }

        [HttpPost("update-user")]
        [Authorize(Policy = "User Manager")]
        public async Task<IActionResult> UpdateUser([FromBody] UpdateUserDto model)
        {
            var sw = Stopwatch.StartNew();
            var user = await _userManager.FindByIdAsync(model.UserId);
            _logger.LogInformation("UpdateUser: FindByIdAsync query took {ElapsedMilliseconds} ms", sw.ElapsedMilliseconds);
            if (user == null)
            {
                _logger.LogWarning("UpdateUser: User not found - UserId={UserId}", model.UserId);
                return NotFound(new { error = "User not found" });
            }

            user.UserName = model.UserName;
            user.PhoneNumber = model.UserName;
            user.FirstName = model.FirstName;
            user.LastName = model.LastName;

            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                _logger.LogError("UpdateUser: Failed to update user UserId={UserId} - Errors: {Errors}", model.UserId, string.Join(", ", updateResult.Errors.Select(e => e.Description)));
                return BadRequest(new { error = "Failed to update user details", details = updateResult.Errors });
            }

            if (!string.IsNullOrEmpty(model.Password))
            {
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                var passwordResult = await _userManager.ResetPasswordAsync(user, token, model.Password);
                if (!passwordResult.Succeeded)
                {
                    _logger.LogError("UpdateUser: Failed to update password for UserId={UserId} - Errors: {Errors}", model.UserId, string.Join(", ", passwordResult.Errors.Select(e => e.Description)));
                    return BadRequest(new { error = "Failed to update password", details = passwordResult.Errors });
                }
            }

            var currentRoles = await _userManager.GetRolesAsync(user);
            var rolesToRemove = currentRoles.Where(r => !model.Roles.Contains(r)).ToList();
            var rolesToAdd = model.Roles.Where(r => !currentRoles.Contains(r)).ToList();

            var removeResult = await _userManager.RemoveFromRolesAsync(user, rolesToRemove);
            if (!removeResult.Succeeded)
            {
                _logger.LogError("UpdateUser: Failed to remove roles for UserId={UserId} - Errors: {Errors}", model.UserId, string.Join(", ", removeResult.Errors.Select(e => e.Description)));
                return BadRequest(new { error = "Failed to remove roles", details = removeResult.Errors });
            }

            var addResult = await _userManager.AddToRolesAsync(user, rolesToAdd);
            if (!addResult.Succeeded)
            {
                _logger.LogError("UpdateUser: Failed to add roles for UserId={UserId} - Errors: {Errors}", model.UserId, string.Join(", ", addResult.Errors.Select(e => e.Description)));
                return BadRequest(new { error = "Failed to add roles", details = addResult.Errors });
            }

            _logger.LogInformation("UpdateUser: Updated user: {UserName}, PhoneNumber: {PhoneNumber} in {TotalElapsedMilliseconds} ms", user.UserName, user.PhoneNumber, sw.ElapsedMilliseconds);
            return Ok(new { message = "User updated successfully" });
        }

        [HttpPost("add-user")]
        [Authorize(Policy = "User Manager")]
        public async Task<IActionResult> AddUser([FromBody] AddUserDto model)
        {
            _logger.LogInformation("AddUser: Received payload - {Payload}", JsonConvert.SerializeObject(model));
            if (model == null || string.IsNullOrEmpty(model.PhoneNumber))
            {
                _logger.LogWarning("AddUser: Model or PhoneNumber is null");
                return BadRequest(new { error = "PhoneNumber is required" });
            }

            var user = new ApplicationUser
            {
                UserName = model.PhoneNumber,
                PhoneNumber = model.PhoneNumber,
                NormalizedUserName = _userManager.NormalizeName(model.PhoneNumber),
                FirstName = model.FirstName,
                LastName = model.LastName,
                EmailConfirmed = true,
                MustChangePassword = true
            };
            _logger.LogInformation("AddUser: Creating user - UserName: {UserName}, PhoneNumber: {PhoneNumber}, MustChangePassword: true", user.UserName, user.PhoneNumber);

            var result = await _userManager.CreateAsync(user, "Pwd1234.");
            if (!result.Succeeded)
            {
                _logger.LogError("AddUser: Failed to create user - Errors: {Errors}", string.Join(", ", result.Errors.Select(e => e.Description)));
                return BadRequest(new { error = "User creation failed", details = result.Errors });
            }

            var createdUser = await _userManager.FindByNameAsync(model.PhoneNumber);
            if (createdUser == null)
            {
                _logger.LogError("AddUser: Failed to find newly created user - UserName: {UserName}", model.PhoneNumber);
                return StatusCode(500, new { error = "Failed to find created user" });
            }

            var roles = new List<string> { "Singer" };
            var addResult = await _userManager.AddToRolesAsync(createdUser, roles);
            if (!addResult.Succeeded)
            {
                _logger.LogError("AddUser: Failed to add roles for UserId={UserId} - Errors: {Errors}", createdUser.Id, string.Join(", ", addResult.Errors.Select(e => e.Description)));
                return BadRequest(new { error = "Failed to add roles", details = addResult.Errors });
            }

            _logger.LogInformation("AddUser: User {UserName} created with role: Singer", createdUser.UserName);
            return Ok(new { message = "User added successfully" });
        }

        [HttpPatch("users/{userId}/force-password-change")]
        [Authorize(Policy = "User Manager")]
        public async Task<IActionResult> ForcePasswordChange(string userId, [FromBody] ForcePasswordChangeDto model)
        {
            var sw = Stopwatch.StartNew();
            var user = await _userManager.FindByIdAsync(userId);
            _logger.LogInformation("ForcePasswordChange: FindByIdAsync query took {ElapsedMilliseconds} ms", sw.ElapsedMilliseconds);
            if (user == null)
            {
                _logger.LogWarning("ForcePasswordChange: User not found - UserId={UserId}", userId);
                return NotFound(new { error = "User not found" });
            }

            user.MustChangePassword = model.MustChangePassword;
            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                _logger.LogError("ForcePasswordChange: Failed to update user UserId={UserId} - Errors: {Errors}", userId, string.Join(", ", updateResult.Errors.Select(e => e.Description)));
                return BadRequest(new { error = "Failed to update user", details = updateResult.Errors });
            }

            _logger.LogInformation("ForcePasswordChange: Updated MustChangePassword to {MustChangePassword} for user: {UserName} in {TotalElapsedMilliseconds} ms", model.MustChangePassword, user.UserName, sw.ElapsedMilliseconds);
            return Ok(new { message = "User updated successfully" });
        }

        [HttpGet("user-details")]
        [Authorize]
        public async Task<IActionResult> GetUserDetails()
        {
            var userName = User.Identity?.Name;
            if (string.IsNullOrEmpty(userName))
            {
                _logger.LogWarning("GetUserDetails: User identity not found");
                return Unauthorized(new { error = "User identity not found" });
            }

            var sw = Stopwatch.StartNew();
            var user = await _userManager.FindByNameAsync(userName);
            _logger.LogInformation("GetUserDetails: FindByNameAsync query took {ElapsedMilliseconds} ms", sw.ElapsedMilliseconds);
            if (user == null)
            {
                _logger.LogWarning("GetUserDetails: User not found - UserName={UserName}", userName);
                return NotFound(new { error = "User not found" });
            }

            var roles = await _userManager.GetRolesAsync(user);
            _logger.LogInformation("GetUserDetails: Returning user details for {UserName} in {TotalElapsedMilliseconds} ms", userName, sw.ElapsedMilliseconds);
            return Ok(new
            {
                id = user.Id,
                userName = user.UserName,
                firstName = user.FirstName,
                lastName = user.LastName,
                mustChangePassword = user.MustChangePassword,
                roles
            });
        }

        [HttpPost("change-password")]
        [Authorize]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto model)
        {
            var userName = User.Identity?.Name;
            if (string.IsNullOrEmpty(userName))
            {
                _logger.LogWarning("ChangePassword: User identity not found");
                return Unauthorized(new { error = "User identity not found" });
            }

            var sw = Stopwatch.StartNew();
            var user = await _userManager.FindByNameAsync(userName);
            _logger.LogInformation("ChangePassword: FindByNameAsync query took {ElapsedMilliseconds} ms", sw.ElapsedMilliseconds);
            if (user == null)
            {
                _logger.LogWarning("ChangePassword: User not found - UserName={UserName}", userName);
                return NotFound(new { error = "User not found" });
            }

            if (!user.MustChangePassword)
            {
                if (string.IsNullOrEmpty(model.CurrentPassword))
                {
                    _logger.LogWarning("ChangePassword: Current password required for user {UserName}", user.UserName);
                    return BadRequest(new { error = "Current password is required" });
                }

                var isPasswordValid = await _userManager.CheckPasswordAsync(user, model.CurrentPassword);
                if (!isPasswordValid)
                {
                    _logger.LogWarning("ChangePassword: Invalid current password for user {UserName}", user.UserName);
                    return BadRequest(new { error = "Invalid current password" });
                }
            }

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, model.NewPassword);
            if (!result.Succeeded)
            {
                _logger.LogError("ChangePassword: Failed to change password for user {UserName} - Errors: {Errors}", user.UserName, string.Join(", ", result.Errors.Select(e => e.Description)));
                return BadRequest(new { error = "Failed to change password", details = result.Errors });
            }

            if (user.MustChangePassword)
            {
                user.MustChangePassword = false;
                var updateResult = await _userManager.UpdateAsync(user);
                if (!updateResult.Succeeded)
                {
                    _logger.LogError("ChangePassword: Failed to reset MustChangePassword for user {UserName} - Errors: {Errors}", user.UserName, string.Join(", ", updateResult.Errors.Select(e => e.Description)));
                    return BadRequest(new { error = "Failed to update user", details = updateResult.Errors });
                }
            }

            _logger.LogInformation("ChangePassword: Password changed successfully for user: {UserName} in {TotalElapsedMilliseconds} ms", user.UserName, sw.ElapsedMilliseconds);
            return Ok(new { message = "Password changed successfully" });
        }

        [HttpGet("registration-settings")]
        [Authorize(Policy = "User Manager")]
        public async Task<IActionResult> GetRegistrationSettings()
        {
            var sw = Stopwatch.StartNew();
            var settings = await _context.RegistrationSettings.AsNoTracking().FirstOrDefaultAsync();
            _logger.LogInformation("GetRegistrationSettings: RegistrationSettings query took {ElapsedMilliseconds} ms", sw.ElapsedMilliseconds);
            if (settings == null)
            {
                _logger.LogWarning("GetRegistrationSettings: No settings found");
                return NotFound(new { error = "Registration settings not found" });
            }

            _logger.LogInformation("GetRegistrationSettings: Returning settings in {TotalElapsedMilliseconds} ms", sw.ElapsedMilliseconds);
            return Ok(new { pinCode = settings.CurrentPin });
        }

        [HttpPatch("registration-settings")]
        [Authorize(Policy = "User Manager")]
        public async Task<IActionResult> UpdateRegistrationSettings([FromBody] UpdateRegistrationSettingsDto model)
        {
            if (string.IsNullOrEmpty(model.PinCode) || model.PinCode.Length != 6 || !model.PinCode.All(char.IsDigit))
            {
                _logger.LogWarning("UpdateRegistrationSettings: Invalid PinCode - {PinCode}", model.PinCode);
                return BadRequest(new { error = "PIN code must be exactly 6 digits" });
            }

            var sw = Stopwatch.StartNew();
            var settings = await _context.RegistrationSettings.FirstOrDefaultAsync();
            _logger.LogInformation("UpdateRegistrationSettings: RegistrationSettings query took {ElapsedMilliseconds} ms", sw.ElapsedMilliseconds);
            if (settings == null)
            {
                settings = new RegistrationSettings { Id = 1, CurrentPin = model.PinCode };
                _context.RegistrationSettings.Add(settings);
            }
            else
            {
                settings.CurrentPin = model.PinCode;
                _context.RegistrationSettings.Update(settings);
            }

            var pinChange = new PinChangeHistory
            {
                Pin = model.PinCode,
                ChangedBy = User.Identity?.Name ?? "Unknown",
                ChangedAt = DateTime.UtcNow
            };
            _context.PinChangeHistory.Add(pinChange);

            await _context.SaveChangesAsync();
            _logger.LogInformation("UpdateRegistrationSettings: PIN code updated to {PinCode} by {UserName} in {TotalElapsedMilliseconds} ms", model.PinCode, User.Identity?.Name ?? "Unknown", sw.ElapsedMilliseconds);
            return Ok(new { message = "PIN code updated successfully" });
        }

        [HttpPost("logout")]
        [Authorize]
        public async Task<IActionResult> Logout()
        {
            try
            {
                var userName = User.Identity?.Name;
                if (string.IsNullOrEmpty(userName))
                {
                    _logger.LogWarning("Logout: User identity not found");
                    return Unauthorized(new { error = "User identity not found" });
                }

                var jti = User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
                _logger.LogInformation("Logout attempt for UserName: {UserName}, JTI: {JTI}", userName, jti ?? "None");

                var sw = Stopwatch.StartNew();
                var user = await _userManager.FindByNameAsync(userName);
                _logger.LogInformation("Logout: FindByNameAsync query took {ElapsedMilliseconds} ms", sw.ElapsedMilliseconds);
                if (user == null)
                {
                    _logger.LogWarning("Logout: User not found - UserName={UserName}", userName);
                    return NotFound(new { error = "User not found" });
                }

                // Mark user as logged out by clearing LastActivity
                user.LastActivity = null;
                var updateResult = await _userManager.UpdateAsync(user);
                if (!updateResult.Succeeded)
                {
                    _logger.LogError("Logout: Failed to update LastActivity for UserName={UserName} - Errors: {Errors}", userName, string.Join(", ", updateResult.Errors.Select(e => e.Description)));
                    return StatusCode(500, new { error = "Failed to update user activity", details = updateResult.Errors });
                }

                var swAttendances = Stopwatch.StartNew();
                var attendances = await _context.EventAttendances
                    .Where(ea => ea.RequestorId == user.Id && ea.IsCheckedIn)
                    .ToListAsync();
                _logger.LogInformation("Logout: EventAttendances query took {ElapsedMilliseconds} ms", swAttendances.ElapsedMilliseconds);

                if (attendances.Any())
                {
                    foreach (var attendance in attendances)
                    {
                        attendance.IsCheckedIn = false;
                    }
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Checked out user {UserName} from {Count} events in {TotalElapsedMilliseconds} ms", userName, attendances.Count, sw.ElapsedMilliseconds);
                }
                else
                {
                    _logger.LogInformation("No active event check-ins found for user {UserName} in {TotalElapsedMilliseconds} ms", userName, sw.ElapsedMilliseconds);
                }

                _logger.LogInformation("Logout successful for UserName: {UserName}, JTI: {JTI} in {TotalElapsedMilliseconds} ms", userName, jti ?? "None", sw.ElapsedMilliseconds);
                return Ok(new { message = "Logout successful" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during logout for UserName: {UserName}", User.Identity?.Name);
                return StatusCode(500, new { error = "Error during logout", details = ex.Message });
            }
        }

        private string GenerateJwtToken(ApplicationUser user, IList<string> roles)
        {
            var issuer = _configuration["JwtSettings:Issuer"];
            var audience = _configuration["JwtSettings:Audience"];
            var keyString = _configuration["JwtSettings:SecretKey"];

            if (string.IsNullOrEmpty(issuer) || string.IsNullOrEmpty(audience) || string.IsNullOrEmpty(keyString))
            {
                _logger.LogError("GenerateJwtToken: Missing JWT configuration");
                throw new InvalidOperationException("JWT configuration is missing.");
            }

            if (string.IsNullOrEmpty(user.UserName))
            {
                _logger.LogError("GenerateJwtToken: UserName is null for user");
                throw new InvalidOperationException("UserName cannot be null");
            }

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyString));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.UserName),
                new Claim(ClaimTypes.Name, user.UserName),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim("firstName", user.FirstName ?? string.Empty),
                new Claim("lastName", user.LastName ?? string.Empty)
            };

            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                // Extend token lifetime to 12 hours to minimize re-authentication during events
                expires: DateTime.UtcNow.AddHours(12),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}