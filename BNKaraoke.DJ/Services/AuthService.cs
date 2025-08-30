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

namespace BNKaraoke.DJ.Services
{
    public class AuthService : IAuthService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<AuthService> _logger;
        private readonly SettingsService _settingsService;

        public AuthService()
        {
            _settingsService = SettingsService.Instance;
            _httpClient = new HttpClient { BaseAddress = new Uri(_settingsService.Settings.ApiUrl) };
            _logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<AuthService>();
            Log.Information("[AUTH] Initialized with ApiUrl={ApiUrl}", _settingsService.Settings.ApiUrl);
        }

        public void SetApiUrl(string apiUrl)
        {
            if (string.IsNullOrWhiteSpace(apiUrl)) return;
            _httpClient.BaseAddress = new Uri(apiUrl);
            _settingsService.Settings.ApiUrl = apiUrl;
            _settingsService.SaveSettings(_settingsService.Settings);
            Log.Information("[AUTH] API base URL set to {ApiUrl}", apiUrl);
        }

        public async Task<LoginResult> LoginAsync(string userName, string password)
        {
            try
            {
                if (string.IsNullOrEmpty(_settingsService.Settings.ApiUrl) || !_settingsService.IsValidUrl(_settingsService.Settings.ApiUrl))
                {
                    Log.Warning("[AUTH] Invalid API URL: {ApiUrl}", _settingsService.Settings.ApiUrl);
                    MessageBox.Show("Invalid API URL configured. Please update in Settings.", "Invalid API URL", MessageBoxButton.OK, MessageBoxImage.Error);
                    var settingsWindow = new SettingsWindow { WindowStartupLocation = WindowStartupLocation.CenterScreen };
                    settingsWindow.ShowDialog();
                    _httpClient.BaseAddress = new Uri(_settingsService.Settings.ApiUrl);
                    Log.Information("[AUTH] Updated API base URL to: {BaseUrl}", _settingsService.Settings.ApiUrl);
                }

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

                // Persist ApiUrl
                var settings = _settingsService.Settings;
                if (!settings.AvailableApiUrls.Contains(settings.ApiUrl))
                {
                    settings.AvailableApiUrls.Add(settings.ApiUrl);
                    await _settingsService.SaveSettingsAsync(settings);
                    Log.Information("[AUTH] Added API URL to AvailableApiUrls: {ApiUrl}", settings.ApiUrl);
                }

                return loginResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Login failed for UserName={UserName}: {Message}", userName, ex.Message);
                Log.Error("[AUTH] Login failed for UserName={UserName}: {Message}, StackTrace={StackTrace}", userName, ex.Message, ex.StackTrace);
                throw new Exception($"Login failed: {ex.Message}", ex);
            }
        }

        public string GetCurrentApiUrl()
        {
            return _httpClient.BaseAddress?.ToString().TrimEnd('/') ?? _settingsService.Settings.ApiUrl;
        }
    }
}