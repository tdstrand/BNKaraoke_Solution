using BNKaraoke.DJ.Models;
using BNKaraoke.DJ.Services;
using BNKaraoke.DJ.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace BNKaraoke.DJ.ViewModels
{
    public partial class LoginWindowViewModel : ObservableObject
    {
        private readonly AuthService _authService = new AuthService();
        private readonly IUserSessionService _userSessionService = UserSessionService.Instance;
        private readonly SettingsService _settingsService = SettingsService.Instance;

        private string _rawUserName = string.Empty;
        private string _userName = string.Empty;
        [ObservableProperty] private string _password = string.Empty;
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private string _errorMessage = string.Empty;

        public string UserName
        {
            get => _userName;
            set
            {
                var digits = Regex.Replace(value, "[^0-9]", "");
                if (digits.Length > 10) digits = digits.Substring(0, 10);

                if (_rawUserName != digits)
                {
                    _rawUserName = digits;
                    string formatted = digits;
                    if (digits.Length >= 7)
                        formatted = $"({digits.Substring(0, 3)}) {digits.Substring(3, 3)}-{digits.Substring(6)}";
                    else if (digits.Length >= 4)
                        formatted = $"({digits.Substring(0, 3)}) {digits.Substring(3)}";
                    else if (digits.Length > 0)
                        formatted = $"({digits}";
                    else
                        formatted = string.Empty;

                    if (SetProperty(ref _userName, formatted))
                    {
                        Log.Information("[LOGIN] UserName set: Raw={Raw}, Display={Display}, CanLogin={CanLogin}", _rawUserName, formatted, CanLogin);
                        OnPropertyChanged(nameof(CanLogin));
                        LoginCommand.NotifyCanExecuteChanged();
                    }
                }
            }
        }

        public bool CanLogin => _rawUserName.Length == 10 && !string.IsNullOrWhiteSpace(Password);

        public LoginWindowViewModel()
        {
            Log.Information("[LOGIN INIT] ViewModel initialized: {InstanceId}", GetHashCode());
        }

        [RelayCommand(CanExecute = nameof(CanLogin))]
        private async Task LoginAsync()
        {
            try
            {
                ErrorMessage = string.Empty;
                IsBusy = true;

                var userNameDigits = _rawUserName;
                Log.Information("[LOGIN] Attempting login with UserName={UserName}", userNameDigits);
                if (string.IsNullOrWhiteSpace(userNameDigits))
                {
                    ErrorMessage = "Username is required.";
                    Log.Error("[LOGIN] Login failed: Empty username");
                    return;
                }
                if (userNameDigits.Length != 10)
                {
                    ErrorMessage = "Username must be a 10-digit phone number.";
                    Log.Error("[LOGIN] Login failed: Invalid username length={Length}", userNameDigits.Length);
                    return;
                }
                if (string.IsNullOrWhiteSpace(Password))
                {
                    ErrorMessage = "Password is required.";
                    Log.Error("[LOGIN] Login failed: Empty password");
                    return;
                }

                // Try login
                var loginResult = await _authService.LoginAsync(userNameDigits, Password);
                if (!string.IsNullOrEmpty(loginResult.Token))
                {
                    _userSessionService.SetSession(loginResult, userNameDigits);
                    Log.Information("[LOGIN] Session set: IsAuthenticated=True");

                    // Save verified ApiUrl from AuthService (set via MiniSettingsWindow)
                    var settings = _settingsService.Settings;
                    var apiUrl = _authService.GetCurrentApiUrl(); // Assume AuthService exposes BaseAddress
                    if (!settings.AvailableApiUrls.Contains(apiUrl))
                    {
                        settings.AvailableApiUrls.Add(apiUrl);
                        settings.ApiUrl = apiUrl;
                        await _settingsService.SaveSettingsAsync(settings);
                        Log.Information("[LOGIN] Added verified API URL: {ApiUrl}", apiUrl);
                    }

                    if (Application.Current.Windows.OfType<LoginWindow>().FirstOrDefault() is LoginWindow loginWindow)
                    {
                        loginWindow.DialogResult = true;
                        Log.Information("[LOGIN] LoginWindow DialogResult set to true");
                    }
                    else
                    {
                        Log.Warning("[LOGIN] No LoginWindow found to close");
                    }
                }
                else
                {
                    ErrorMessage = "Invalid username or password.";
                    Log.Error("[LOGIN] Login failed: Invalid credentials");
                }
            }
            catch (HttpRequestException ex)
            {
                ErrorMessage = $"Login failed: {ex.Message}. Check API URL in settings.";
                Log.Error("[LOGIN] Login failed: HttpRequestException: {Message}", ex.Message);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Login failed: {ex.Message}";
                Log.Error("[LOGIN] Login failed: {Message}", ex.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}