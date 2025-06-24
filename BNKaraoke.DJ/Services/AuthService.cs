using BNKaraoke.DJ.Models;
using BNKaraoke.DJ.Views;
using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace BNKaraoke.DJ.Services;

public class AuthService : IAuthService
{
    private HttpClient _httpClient;
    private readonly ILogger<AuthService> _logger;

    public AuthService()
    {
        _httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:7290") };
        _logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<AuthService>();
    }

    public async Task<LoginResult> LoginAsync(string userName, string password)
    {
        while (true)
        {
            try
            {
                var request = new { UserName = userName, Password = password };
                var requestJson = JsonSerializer.Serialize(request);
                Log.Information("[AUTH] Sending login request for UserName={UserName}, Payload={Payload}", userName, requestJson);
                var response = await _httpClient.PostAsJsonAsync("/api/auth/login", request);
                Log.Information("[AUTH] Login response status: {StatusCode}", response.StatusCode);
                response.EnsureSuccessStatusCode();

                var loginResult = await response.Content.ReadFromJsonAsync<LoginResult>();
                Log.Information("[AUTH] Login response: Token={Token}, FirstName={FirstName}, Roles={Roles}",
                    loginResult?.Token?.Substring(0, Math.Min(10, loginResult.Token?.Length ?? 0)) ?? "null",
                    loginResult?.FirstName,
                    loginResult?.Roles != null ? string.Join(",", loginResult.Roles) : "null");

                if (loginResult == null || string.IsNullOrEmpty(loginResult.Token))
                {
                    _logger.LogWarning("Login failed: No token received.");
                    Log.Warning("[AUTH] Login failed: No token received for UserName={UserName}", userName);
                    throw new Exception("Login failed: Invalid response from server.");
                }

                _logger.LogInformation("Login successful for user: {UserName}", userName);
                Log.Information("[AUTH] Login successful: Token={Token}, FirstName={FirstName}, Roles={Roles}",
                    loginResult.Token?.Substring(0, Math.Min(10, loginResult.Token.Length)) ?? "null",
                    loginResult.FirstName,
                    loginResult.Roles != null ? string.Join(",", loginResult.Roles) : "null");

                return loginResult;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Login failed due to network error.");
                Log.Error("[AUTH] Login failed due to network error for UserName={UserName}: {Message}", userName, ex.Message);
                var miniSettings = new MiniSettingsWindow { WindowStartupLocation = WindowStartupLocation.CenterScreen };
                if (miniSettings.ShowDialog() == true && !string.IsNullOrEmpty(miniSettings.BaseUrl))
                {
                    try
                    {
                        _httpClient = new HttpClient { BaseAddress = new Uri(miniSettings.BaseUrl) };
                        Log.Information("[AUTH] Updated API base URL to: {BaseUrl}", miniSettings.BaseUrl);
                        continue;
                    }
                    catch (UriFormatException)
                    {
                        Log.Error("[AUTH] Invalid base URL provided: {BaseUrl}", miniSettings.BaseUrl);
                    }
                }
                throw new Exception("Login failed: Unable to connect to the server.", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Login failed.");
                Log.Error("[AUTH] Login failed for UserName={UserName}: {Message}", userName, ex.Message);
                throw new Exception("Login failed: An unexpected error occurred.", ex);
            }
        }
    }
}